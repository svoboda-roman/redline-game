using System;

namespace Redline.Core
{
    /// <summary>
    /// Big Tentacle Bill (<see cref="StoryBoss"/>), the story boss in the room marked 'B'. Walking into the room wakes
    /// it; each of its seven tentacles falls off after storyBossTentacleHits strikes (or reflected mud clumps); with all
    /// of them gone the body takes damage, and when its HP runs out it dies and the story so far is complete. Attacks:
    /// a tentacle swipe along the floor, a tentacle lunge at the player, a body slam with a shockwave running both ways
    /// along the floor, and a volley of mud clumps (strike them back at it). Dying resets it (back to the checkpoint).
    /// </summary>
    public sealed partial class RedlineSim
    {
        /// <summary>The story boss, or null on maps without a boss room.</summary>
        public StoryBoss Boss { get; private set; }

        private float _bossClock;

        /// <summary>Builds (or rebuilds) the boss in its room, asleep, at full health.</summary>
        private void BossReset()
        {
            var s = _map.Story;
            UnsealBossRoom();
            if (s?.Boss == null) { Boss = null; return; }
            var m = s.Boss.Value;
            var b = Boss ?? new StoryBoss();
            int mx = (int)MathF.Floor(m.X), my = (int)MathF.Floor(m.Y);
            int fy = my;
            while (fy > 0 && !_map.IsSolid(mx, fy - 1)) fy--;
            int x0 = mx, x1 = mx;
            while (x0 > 0 && !_map.IsSolid(x0 - 1, fy + 1)) x0--;
            while (x1 < _map.Width - 1 && !_map.IsSolid(x1 + 1, fy + 1)) x1++;
            b.FloorY = fy;
            b.RoomMinX = x0;
            b.RoomMaxX = x1 + 1;
            b.Radius = 3.2f;
            b.GroundY = fy + b.Radius * 0.85f;
            b.Home = new Vec2(b.RoomMaxX - b.Radius - 1.5f, b.GroundY);
            b.StandLeft = 1;
            b.SpinAngle = 0f;
            b.Body = b.Home;
            b.MaxHp = _cfg.storyBossHp;
            b.Hp = b.MaxHp;
            b.Move = BossMove.Dormant;
            b.MoveTime = 0f;
            b.MoveTentacle = -1;
            b.Cooldown = 0f;
            b.Flash = 0f;
            b.LastStrikeId = -1;
            b.WaveLeft = b.WaveRight = float.NaN;
            // Seven tentacles fanned out toward the room (the player's side), one reaching up.
            float[] angles = { 100f, 128f, 150f, 170f, 190f, 212f, 236f };
            for (int i = 0; i < StoryBoss.TentacleCount; i++)
            {
                var t = b.Tentacles[i];
                float a = angles[i] * (MathF.PI / 180f);
                t.BaseAngle = angles[i];
                t.RootOffset = new Vec2(MathF.Cos(a), MathF.Sin(a)) * (b.Radius * 0.75f);
                t.Length = 7f + (i % 3) * 1.2f;
                t.Thickness = 0.6f;
                t.Hits = 0;
                t.Severed = false;
                t.Flash = 0f;
                t.LastStrikeId = -1;
            }
            Boss = b;
            _bossClock = 0f;
            PoseTentacles(b);
        }

        private readonly System.Collections.Generic.List<(int x, int y)> _bossSeal = new System.Collections.Generic.List<(int x, int y)>();

        /// <summary>Floor cells closed with stone when the fight starts (the shaft the player climbed in by); the view draws them.</summary>
        public System.Collections.Generic.IReadOnlyList<(int x, int y)> BossSeal => _bossSeal;

        /// <summary>
        /// The fight starts: the player is put in the middle of the room (left of the boss, on the floor) and every hole in
        /// the room's floor is closed with stone, so there is no falling back down the shaft.
        /// </summary>
        private void SealBossRoom(StoryBoss b)
        {
            int fy = (int)b.FloorY;
            for (int x = (int)b.RoomMinX; x < (int)b.RoomMaxX; x++)
            {
                if (_map.IsSolid(x, fy - 1)) continue;
                _map.Set(x, fy - 1, Tile.Solid);
                _bossSeal.Add((x, fy - 1));
            }
            var p = Player;
            ReleaseHook();
            p.X = b.RoomMinX + (b.Home.X - b.Radius - b.RoomMinX) * 0.45f;
            p.Y = b.FloorY;
            p.Vx = 0f;
            p.Vy = 0f;
            p.RefreshContacts(_map);
        }

        /// <summary>Opens the floor again (the boss was reset: the player climbs back up from the checkpoint).</summary>
        private void UnsealBossRoom()
        {
            foreach (var c in _bossSeal) _map.Set(c.x, c.y, Tile.Empty);
            _bossSeal.Clear();
        }

        /// <summary>True when the player is inside the boss room.</summary>
        private bool InBossRoom(StoryBoss b)
        {
            var p = Player;
            return p.X >= b.RoomMinX && p.X <= b.RoomMaxX && p.Y >= b.FloorY - 0.1f && p.Y < b.FloorY + 20f;
        }

        /// <summary>Per step on a story map with a boss: wake, think, attack, die.</summary>
        private void BossUpdate(float dt)
        {
            var b = Boss;
            if (b == null) return;
            var c = _cfg;
            _bossClock += dt;
            b.Flash = MathF.Max(0f, b.Flash - dt * 3f);
            foreach (var t in b.Tentacles) t.Flash = MathF.Max(0f, t.Flash - dt * 3f);

            if (b.Move == BossMove.Dormant)
            {
                PoseTentacles(b);
                if (InBossRoom(b))
                {
                    b.Move = BossMove.Idle;
                    b.Cooldown = 1.6f;
                    SealBossRoom(b);
                    Emit(SimEventType.BossWake, b.Body);
                }
                return;
            }
            if (b.Move == BossMove.Dying)
            {
                b.MoveTime += dt;
                b.Body = new Vec2(b.Home.X, b.GroundY - b.MoveTime * 1.2f);
                if (b.MoveTime >= 2.5f)
                {
                    State = SimState.Victory;
                    Emit(SimEventType.StoryComplete, Player.Center);
                }
                return;
            }

            b.MoveTime += dt;
            bool rage = b.Vulnerable;   // every tentacle gone: faster and meaner
            switch (b.Move)
            {
                case BossMove.Idle:
                    b.Facing = Player.X > b.Body.X ? 1 : -1;   // turns to face the player between attacks
                    b.Cooldown -= dt;
                    if (b.Cooldown <= 0f) ChooseBossMove(b);
                    break;
                case BossMove.Chase:
                {
                    // Lurches after the player, stopping just out of body contact.
                    float speed = c.storyBossChaseSpeed * (rage ? 1.5f : 1f);
                    float dx = Player.X - b.Body.X;
                    b.Facing = dx > 0f ? 1 : -1;
                    if (MathF.Abs(dx) > b.Radius + 1.2f) MoveBody(b, b.Body.X + MathF.Sign(dx) * speed * dt);
                    b.Body = new Vec2(b.Body.X, b.GroundY + MathF.Abs(MathF.Sin(b.MoveTime * 7f)) * 0.35f);
                    if (b.MoveTime >= b.ChaseTime) EndBossMove(b, 0.35f);
                    break;
                }
                case BossMove.Swipe:
                    if (b.MoveTime >= 0.7f + 0.45f + 0.5f || b.Tentacles[b.MoveTentacle].Severed) EndBossMove(b);
                    break;
                case BossMove.Lunge:
                    if (b.MoveTime >= 0.6f + 0.2f + 0.3f + 0.4f || b.Tentacles[b.MoveTentacle].Severed) EndBossMove(b);
                    break;
                case BossMove.Spin:
                {
                    // Curls in (telegraph 0.6 s), whirls for 1.4 s, unwinds 0.4 s.
                    float t = b.MoveTime;
                    if (t < 0.6f) b.SpinAngle = -35f * Ease(t / 0.6f);
                    else if (t < 2.0f) b.SpinAngle += (rage ? 520f : 420f) * dt;
                    else b.SpinAngle = Lerp(b.SpinAngle, MathF.Round(b.SpinAngle / 360f) * 360f, (t - 2.0f) / 0.4f);
                    if (t >= 2.4f) { b.SpinAngle = 0f; EndBossMove(b); }
                    break;
                }
                case BossMove.Leap:
                {
                    // Crouch and whirl (0.8 s), a high slow arc to where the player stood (1.4 s: time to get out of the
                    // way), a crash with a shockwave, then 0.7 s to recover.
                    const float Crouch = 0.8f, Flight = 1.4f;
                    float t = b.MoveTime;
                    if (t < Crouch)
                    {
                        b.Body = new Vec2(b.Body.X, b.GroundY - 0.6f * Ease(t / Crouch));
                        b.SpinAngle += 300f * Ease(t / Crouch) * dt;
                    }
                    else if (t < Crouch + Flight)
                    {
                        float u = (t - Crouch) / Flight;
                        MoveBody(b, Lerp(b.LeapFrom, b.LeapTo, u));
                        b.Body = new Vec2(b.Body.X, b.GroundY + 4f * 9f * u * (1f - u));
                        b.SpinAngle += 520f * dt;
                    }
                    else
                    {
                        if (t - dt < Crouch + Flight)
                        {
                            MoveBody(b, b.LeapTo);
                            b.Body = new Vec2(b.Body.X, b.GroundY);
                            StartShockwave(b);
                        }
                        b.SpinAngle = Lerp(b.SpinAngle, MathF.Round(b.SpinAngle / 360f) * 360f, (t - Crouch - Flight) / 0.5f);
                        if (t >= Crouch + Flight + 0.7f) { b.SpinAngle = 0f; EndBossMove(b); }
                    }
                    break;
                }
                case BossMove.Slam:
                {
                    const float Up = 0.7f, Hold = 0.2f, Down = 0.15f;
                    float t = b.MoveTime;
                    float lift = t < Up ? 3.5f * Ease(t / Up) : t < Up + Hold ? 3.5f : t < Up + Hold + Down ? 3.5f * (1f - (t - Up - Hold) / Down) : 0f;
                    b.Body = new Vec2(b.Body.X, b.GroundY + lift);
                    if (t >= Up + Hold + Down && t - dt < Up + Hold + Down) StartShockwave(b);
                    if (t >= Up + Hold + Down + 0.6f) EndBossMove(b);
                    break;
                }
                case BossMove.Spit:
                {
                    // A clump every 0.45 s (0.32 s enraged) after a 0.5 s wind-up, from the mouth on the player's side.
                    float every = rage ? 0.32f : 0.45f;
                    float t = b.MoveTime - 0.5f;
                    int due = t < 0f ? 0 : 1 + (int)(t / every);
                    int fired = b.ShotsTotal - b.ShotsLeft;
                    if (b.ShotsLeft > 0 && due > fired)
                    {
                        b.ShotsLeft--;
                        float side = Player.X < b.Body.X ? -1f : 1f;
                        var mouth = new Vec2(b.Body.X + side * b.Radius * 0.7f, b.Body.Y + 0.4f);
                        var d = Player.Center - mouth;
                        float len = MathF.Max(1e-3f, d.Length);
                        var shot = SpawnProjectile(mouth, d * (c.storyBossMudSpeed / len));
                        if (shot != null) shot.Mud = true;
                    }
                    if (b.ShotsLeft <= 0 && b.MoveTime > 0.5f + b.ShotsTotal * every + 0.3f) EndBossMove(b);
                    break;
                }
                case BossMove.MudRain:
                {
                    // Hurls mud up at the ceiling (0.5 s); clumps rain down around the player, one every 0.22 s.
                    float t = b.MoveTime - 0.5f;
                    int due = t < 0f ? 0 : 1 + (int)(t / 0.22f);
                    int fired = b.ShotsTotal - b.ShotsLeft;
                    if (b.ShotsLeft > 0 && due > fired)
                    {
                        b.ShotsLeft--;
                        float x = Math.Clamp(Player.X + (NextRandom() - 0.5f) * 14f, b.RoomMinX + 1f, b.RoomMaxX - 1f);
                        var shot = SpawnProjectile(new Vec2(x, b.FloorY + 17f), new Vec2(0f, -c.storyBossMudSpeed * 1.2f));
                        if (shot != null) { shot.Mud = true; shot.TimeLeft = 4f; }
                    }
                    if (b.ShotsLeft <= 0 && b.MoveTime > 0.5f + b.ShotsTotal * 0.22f + 0.5f) EndBossMove(b);
                    break;
                }
            }

            // Shockwave fronts run out along the floor; they hurt a player standing on it.
            if (!float.IsNaN(b.WaveLeft))
            {
                b.WaveLeft -= c.storyBossWaveSpeed * dt;
                b.WaveRight += c.storyBossWaveSpeed * dt;
                var p = Player;
                bool low = p.Y < b.FloorY + 0.9f;
                if (low && p.InvulnTimer <= 0f && (MathF.Abs(p.X - b.WaveLeft) < 0.7f || MathF.Abs(p.X - b.WaveRight) < 0.7f))
                    Hurt(c.storyBossDamage, p.X < b.Home.X ? -1 : 1, false);
                if (b.WaveLeft < b.RoomMinX && b.WaveRight > b.RoomMaxX) b.WaveLeft = b.WaveRight = float.NaN;
            }

            PoseTentacles(b);
            BossContacts(b);
        }

        /// <summary>
        /// The fight's rhythm: a chase (it crawls after the player for 2-3.5 s), then a few attacks where it stopped -
        /// near the player: whirl all tentacles, a swipe or a lunge, or mud; further off: mud, a leap or a slam - then the
        /// next chase. With every tentacle gone it chases faster, stands longer and adds mud rain to the leaps and slams.
        /// </summary>
        private void ChooseBossMove(StoryBoss b)
        {
            var p = Player;
            bool rage = b.Vulnerable;
            b.MoveTime = 0f;
            if (b.StandLeft <= 0)
            {
                b.Move = BossMove.Chase;
                b.ChaseTime = rage ? 1.6f + NextRandom() : 2.2f + NextRandom() * 1.3f;
                b.StandLeft = rage ? 3 : 2;
                return;
            }
            b.StandLeft--;
            float r = NextRandom();
            bool near = MathF.Abs(p.X - b.Body.X) < 10f;
            int tentacle = NearestTentacle(b, p.Center);
            if (rage)
            {
                b.Move = r < 0.28f ? BossMove.Spit : r < 0.52f ? BossMove.MudRain : r < 0.82f ? BossMove.Leap : BossMove.Slam;
            }
            else if (near)
            {
                b.Move = r < 0.32f ? BossMove.Spin : r < 0.57f ? BossMove.Swipe : r < 0.8f ? BossMove.Lunge : BossMove.Spit;
                if ((b.Move == BossMove.Swipe || b.Move == BossMove.Lunge) && tentacle < 0) b.Move = BossMove.Spin;
            }
            else
            {
                b.Move = r < 0.35f ? BossMove.Spit : r < 0.7f ? BossMove.Leap : r < 0.85f ? BossMove.Slam : BossMove.Lunge;
                if (b.Move == BossMove.Lunge && tentacle < 0) b.Move = BossMove.Spit;
            }
            switch (b.Move)
            {
                case BossMove.Swipe:
                case BossMove.Lunge:
                    b.MoveTentacle = tentacle;
                    b.MoveTarget = p.Center;
                    break;
                case BossMove.Spit:
                    b.ShotsTotal = b.ShotsLeft = rage ? 5 : 3;
                    break;
                case BossMove.MudRain:
                    b.ShotsTotal = b.ShotsLeft = 9;
                    break;
                case BossMove.Leap:
                    b.LeapFrom = b.Body.X;
                    b.LeapTo = ClampBodyX(b, p.X);
                    break;
            }
            Emit(SimEventType.BossWindup, b.Body, PlayerTier, (int)b.Move);
        }

        private void EndBossMove(StoryBoss b, float cooldownScale = 1f)
        {
            b.Move = BossMove.Idle;
            b.MoveTime = 0f;
            b.MoveTentacle = -1;
            b.SpinAngle = 0f;
            b.Body = new Vec2(b.Body.X, b.GroundY);
            b.Home = b.Body;
            float cd = _cfg.storyBossCooldown * (0.8f + 0.4f * NextRandom()) * cooldownScale;
            b.Cooldown = b.Vulnerable ? cd * 0.6f : cd;   // all tentacles off: it gets desperate
        }

        /// <summary>The body's x kept inside the room (its whole radius clear of the walls).</summary>
        private static float ClampBodyX(StoryBoss b, float x) =>
            Math.Clamp(x, b.RoomMinX + b.Radius + 0.4f, b.RoomMaxX - b.Radius - 0.4f);

        private static void MoveBody(StoryBoss b, float x)
        {
            x = ClampBodyX(b, x);
            b.Body = new Vec2(x, b.Body.Y);
            b.Home = new Vec2(x, b.GroundY);
        }

        /// <summary>Two shockwave fronts run out along the floor from under the body.</summary>
        private void StartShockwave(StoryBoss b)
        {
            b.WaveLeft = b.Body.X - b.Radius;
            b.WaveRight = b.Body.X + b.Radius;
            Emit(SimEventType.BossSlam, new Vec2(b.Body.X, b.FloorY));
        }

        private int NearestTentacle(StoryBoss b, Vec2 to)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < b.Tentacles.Length; i++)
            {
                var t = b.Tentacles[i];
                if (t.Severed) continue;
                var tip = t.Points[BossTentacle.PointCount - 1];
                float d = (tip - to).Length;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Lays out every tentacle's points: swaying at rest, rearing and sweeping in a swipe, stabbing in a lunge.</summary>
        private void PoseTentacles(StoryBoss b)
        {
            float time = _bossClock;
            for (int i = 0; i < b.Tentacles.Length; i++)
            {
                var t = b.Tentacles[i];
                // Spinning (whirl, leap): the whole fan turns round the body, tentacles stretched out.
                float spin = b.SpinAngle;
                float sr = spin * (MathF.PI / 180f);
                bool flip = b.Facing > 0;   // angles are worked out facing left, then mirrored when it faces right
                var off = spin == 0f ? t.RootOffset
                    : new Vec2(t.RootOffset.X * MathF.Cos(sr) - t.RootOffset.Y * MathF.Sin(sr), t.RootOffset.X * MathF.Sin(sr) + t.RootOffset.Y * MathF.Cos(sr));
                if (flip) off = new Vec2(-off.X, off.Y);
                var root = b.Body + off;
                float baseA = t.BaseAngle + spin, length = t.Length, sway = 22f, curl = 1f, speed = 1.6f;
                bool whirling = b.Move == BossMove.Spin && b.MoveTime >= 0.6f && b.MoveTime < 2.0f || b.Move == BossMove.Leap;
                if (b.Move == BossMove.Spin && b.MoveTime < 0.6f) length = t.Length * Lerp(1f, 0.6f, b.MoveTime / 0.6f);   // curl in
                if (whirling) { length = t.Length * 1.25f; sway = 10f; curl = 0.5f; }
                if (b.Move == BossMove.Chase)
                {
                    // Crawling: the tentacles paw at the floor toward the player like legs.
                    float toward = Player.X < b.Body.X ? 1f : -1f;
                    baseA = t.BaseAngle + toward * 8f * MathF.Sin(time * 7f + i);
                    sway = 30f; speed = 6f;
                }
                if (b.MoveTentacle == i && b.Move == BossMove.Swipe)
                {
                    float mt = b.MoveTime;
                    if (mt < 0.7f) { baseA = Lerp(t.BaseAngle, 95f, Ease(mt / 0.7f)); sway = 6f; }
                    else if (mt < 1.15f) { baseA = Lerp(95f, 192f, Ease((mt - 0.7f) / 0.45f)); length = t.Length * 1.5f; sway = 4f; curl = 0.3f; }
                    else { baseA = Lerp(192f, t.BaseAngle, Ease((mt - 1.15f) / 0.5f)); }
                }
                else if (b.MoveTentacle == i && b.Move == BossMove.Lunge)
                {
                    var d = b.MoveTarget - root;
                    float aim = MathF.Atan2(d.Y, d.X) * (180f / MathF.PI);
                    if (flip) aim = 180f - aim;   // into the facing-left frame
                    float reach = MathF.Min(18f, d.Length + 1.5f);
                    float mt = b.MoveTime;
                    sway = 2f; curl = 0.1f;
                    if (mt < 0.6f) { baseA = Lerp(t.BaseAngle, aim, Ease(mt / 0.6f)); length = t.Length * Lerp(1f, 0.55f, mt / 0.6f); }
                    else if (mt < 0.8f) { baseA = aim; length = Lerp(t.Length * 0.55f, reach, (mt - 0.6f) / 0.2f); }
                    else if (mt < 1.1f) { baseA = aim; length = reach; }
                    else { baseA = Lerp(aim, t.BaseAngle, Ease((mt - 1.1f) / 0.4f)); length = Lerp(reach, t.Length, Ease((mt - 1.1f) / 0.4f)); }
                }
                float seg = length / (BossTentacle.PointCount - 1);
                var pt = root;
                float phase = i * 1.37f;
                for (int k = 0; k < BossTentacle.PointCount; k++)
                {
                    t.Points[k] = pt;
                    float s = k / (float)(BossTentacle.PointCount - 1);
                    float deg = baseA + curl * sway * MathF.Sin(time * speed + phase + s * 2.8f) * s;
                    float a = (flip ? 180f - deg : deg) * (MathF.PI / 180f);
                    pt += new Vec2(MathF.Cos(a), MathF.Sin(a)) * seg;
                    if (pt.Y < b.FloorY + 0.25f) pt = new Vec2(pt.X, b.FloorY + 0.25f);   // lies on the floor, never through it
                }
            }
        }

        /// <summary>The body hurts on contact; a tentacle hurts while it swipes or lunges.</summary>
        private void BossContacts(StoryBoss b)
        {
            var p = Player;
            if (p.InvulnTimer > 0f || p.Hp <= 0) return;
            var pc = p.Center;
            if (BoxCircle(p, b.Body, b.Radius * 0.9f))
            {
                Hurt(_cfg.storyBossDamage, pc.X < b.Body.X ? -1 : 1, false);
                return;
            }
            // Whirling (spin, leap): every tentacle hits.
            bool whirl = b.Move == BossMove.Spin && b.MoveTime >= 0.6f && b.MoveTime < 2.0f
                      || b.Move == BossMove.Leap && b.MoveTime >= 0.8f && b.MoveTime < 2.2f;
            if (whirl)
            {
                foreach (var w in b.Tentacles)
                {
                    if (w.Severed) continue;
                    for (int k = 2; k < BossTentacle.PointCount; k++)
                    {
                        if (!BoxCircle(p, w.Points[k], w.Thickness * 0.9f)) continue;
                        Hurt(_cfg.storyBossDamage, pc.X < b.Body.X ? -1 : 1, false);
                        return;
                    }
                }
                return;
            }
            if (b.MoveTentacle < 0) return;
            bool active = b.Move == BossMove.Swipe && b.MoveTime >= 0.7f && b.MoveTime < 1.15f
                       || b.Move == BossMove.Lunge && b.MoveTime >= 0.6f && b.MoveTime < 1.1f;
            if (!active) return;
            var t = b.Tentacles[b.MoveTentacle];
            if (t.Severed) return;
            for (int k = 2; k < BossTentacle.PointCount; k++)
            {
                if (!BoxCircle(p, t.Points[k], t.Thickness * 0.9f)) continue;
                Hurt(_cfg.storyBossDamage, pc.X < t.Points[k].X ? -1 : 1, false);
                return;
            }
        }

        /// <summary>A sword strike on the boss: tentacles take hits until they fall off; the body only once they are all gone.
        /// Returns true when the strike was shrugged off (a clang: the body while tentacles remain).</summary>
        private bool StrikeBoss(Player p, int tier, float minX, float minY, float maxX, float maxY)
        {
            var b = Boss;
            if (b == null || !b.Fighting) return false;
            for (int i = 0; i < b.Tentacles.Length; i++)
            {
                var t = b.Tentacles[i];
                if (t.Severed || t.LastStrikeId == p.StrikeId) continue;
                for (int k = 1; k < BossTentacle.PointCount; k++)
                {
                    var q = t.Points[k];
                    float r = t.Thickness;
                    if (q.X + r < minX || q.X - r > maxX || q.Y + r < minY || q.Y - r > maxY) continue;
                    t.LastStrikeId = p.StrikeId;
                    HitTentacle(b, i, q);
                    break;
                }
            }
            if (b.LastStrikeId == p.StrikeId) return false;
            float cx = Math.Clamp(b.Body.X, minX, maxX), cy = Math.Clamp(b.Body.Y, minY, maxY);
            float dx = b.Body.X - cx, dy = b.Body.Y - cy;
            if (dx * dx + dy * dy > b.Radius * b.Radius) return false;
            b.LastStrikeId = p.StrikeId;
            if (!b.Vulnerable)
            {
                b.Flash = 0.5f;
                return true;   // the tentacles shield it
            }
            DamageBoss(b, CombatRules.Damage(tier, _cfg));
            return false;
        }

        private void HitTentacle(StoryBoss b, int i, Vec2 at)
        {
            var t = b.Tentacles[i];
            t.Hits++;
            t.Flash = 1f;
            if (t.Hits >= _cfg.storyBossTentacleHits)
            {
                t.Severed = true;
                Emit(SimEventType.BossTentacleCut, at, PlayerTier, i);
                if (b.MoveTentacle == i) EndBossMove(b);
                if (b.Vulnerable) Emit(SimEventType.BossExposed, b.Body);
            }
            else Emit(SimEventType.BossTentacleHit, at, PlayerTier, i);
        }

        private void DamageBoss(StoryBoss b, float damage)
        {
            b.Hp -= damage;
            b.Flash = 1f;
            Emit(SimEventType.BossHurt, b.Body);
            if (b.Hp > 0f) return;
            b.Hp = 0f;
            b.Move = BossMove.Dying;
            b.MoveTime = 0f;
            b.WaveLeft = b.WaveRight = float.NaN;
            Emit(SimEventType.BossDefeated, b.Body);
        }

        /// <summary>A reflected mud clump hits the boss: a tentacle in its way, else the body (only once it is exposed).</summary>
        private bool BossCatchesProjectile(Projectile pr)
        {
            var b = Boss;
            if (b == null || !b.Fighting || !pr.Reflected) return false;
            var pc = pr.Center;
            for (int i = 0; i < b.Tentacles.Length; i++)
            {
                var t = b.Tentacles[i];
                if (t.Severed) continue;
                for (int k = 1; k < BossTentacle.PointCount; k++)
                    if ((t.Points[k] - pc).Length < t.Thickness + 0.3f)
                    {
                        HitTentacle(b, i, pc);
                        return true;
                    }
            }
            if ((b.Body - pc).Length > b.Radius) return false;
            if (b.Vulnerable) DamageBoss(b, _cfg.reflectedProjectileDamage);
            else b.Flash = 0.5f;
            return true;
        }

        private static bool BoxCircle(Body p, Vec2 c, float r)
        {
            float cx = Math.Clamp(c.X, p.MinX, p.MaxX), cy = Math.Clamp(c.Y, p.Y, p.MaxY);
            float dx = c.X - cx, dy = c.Y - cy;
            return dx * dx + dy * dy < r * r;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
        private static float Ease(float t) { t = Math.Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
    }
}
