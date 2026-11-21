using Redline.Core;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Redline.Game
{
    /// <summary>
    /// Reads keyboard and gamepad every rendered frame and hands the simulation one
    /// <see cref="InputFrame"/> per fixed step. Button presses are latched until a step
    /// consumes them, so a press is never lost when a frame runs no step. Keyboard keys come from
    /// a <see cref="ControlBindings"/> table (rebindable in the settings); mouse and gamepad are fixed.
    /// </summary>
    /// <example>
    /// <code>
    /// var input = new RedlineInput(GameSettings.Current.Bindings);
    /// input.Poll();                         // once per Update
    /// if (input.PausePressed) OpenPause();  // this frame only
    /// sim.Step(input.Consume(), dt);        // once per fixed step
    /// </code>
    /// </example>
    /// <remarks>
    /// Default keyboard: A/D or arrows move, Space/W/Up jump, J/Z or left mouse attack,
    /// K/X or right mouse hold to hook, S/Down/Shift slide, R or Enter end the run, Esc pause.
    /// Gamepad: stick or d-pad move, South jump, West attack, right shoulder/trigger hook,
    /// East or left trigger slide, Select/View end the run, Start/Options pause.
    /// </remarks>
    public sealed class RedlineInput
    {
        private readonly ControlBindings _bindings;
        private float _moveX;
        private float _moveY;
        /// <summary>The latched jump press came only from W / Up (reels the hook in while hanging).</summary>
        private bool _jumpByUpOnly;
        private bool _jumpHeld;
        private bool _hookHeld;
        private bool _jumpPressed;
        private bool _attackPressed;
        private bool _slidePressed;
        private bool _hookPressed;
        private bool _restartPressed;
        /// <summary>Restart is held this frame (the training skips a step on a long hold).</summary>
        private bool _restartHeld;

#if ENABLE_INPUT_SYSTEM
        // Resolved keys per action, rebuilt when the binding table's Version changes.
        private readonly Key[][] _keys = new Key[ControlBindings.Actions.Length][];
        private readonly int[] _keyCounts = new int[ControlBindings.Actions.Length];
        private int _bindingsVersion = -1;
#endif

        /// <summary>Creates the reader over a binding table (kept by reference, so rebinding applies at once).</summary>
        public RedlineInput(ControlBindings bindings)
        {
            _bindings = bindings ?? ControlBindings.CreateDefaults();
#if ENABLE_INPUT_SYSTEM
            for (int i = 0; i < _keys.Length; i++) _keys[i] = new Key[ControlBindings.SlotsPerAction];
#endif
        }

        /// <summary>The pause key or Start was pressed this frame (not latched).</summary>
        public bool PausePressed { get; private set; }

        /// <summary>Samples devices. Call once per rendered frame.</summary>
        public void Poll()
        {
#if ENABLE_INPUT_SYSTEM
            ResolveBindings();
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            float move = 0f;
            if (Held(kb, GameAction.MoveLeft)) move -= 1f;
            if (Held(kb, GameAction.MoveRight)) move += 1f;
            if (pad != null)
            {
                float stick = pad.leftStick.ReadValue().x;
                if (Mathf.Abs(stick) > 0.3f) move = stick;
                if (pad.dpad.left.isPressed) move = -1f;
                if (pad.dpad.right.isPressed) move = 1f;
            }
            _moveX = Mathf.Clamp(move, -1f, 1f);

            // Vertical (hook reel): W / Up arrow up, S / Down arrow down, or the left stick / d-pad.
            float vy = 0f;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) vy += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) vy -= 1f;
            }
            if (pad != null)
            {
                float sy = pad.leftStick.ReadValue().y;
                if (Mathf.Abs(sy) > 0.4f) vy = sy;
                if (pad.dpad.up.isPressed) vy = 1f;
                if (pad.dpad.down.isPressed) vy = -1f;
            }
            _moveY = Mathf.Clamp(vy, -1f, 1f);
            bool upDown = kb != null && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame);
            bool otherJumpDown = (kb != null && kb.spaceKey.wasPressedThisFrame) || Down(pad?.buttonSouth);
            if (Down(kb, GameAction.Jump) || Down(pad?.buttonSouth)) _jumpByUpOnly = (_jumpByUpOnly || !_jumpPressed) && upDown && !otherJumpDown;

            _jumpHeld = Held(kb, GameAction.Jump) || Held(pad?.buttonSouth);
            _hookHeld = Held(kb, GameAction.Hook) || Held(mouse?.rightButton)
                        || Held(pad?.rightShoulder) || Held(pad?.rightTrigger);

            _jumpPressed |= Down(kb, GameAction.Jump) || Down(pad?.buttonSouth);
            _attackPressed |= Down(kb, GameAction.Attack) || Down(mouse?.leftButton) || Down(pad?.buttonWest);
            _slidePressed |= Down(kb, GameAction.Slide) || Down(pad?.buttonEast) || Down(pad?.leftTrigger);
            _hookPressed |= Down(kb, GameAction.Hook) || Down(mouse?.rightButton)
                            || Down(pad?.rightShoulder) || Down(pad?.rightTrigger);
            _restartPressed |= Down(kb, GameAction.Restart) || Down(pad?.selectButton);
            _restartHeld = Held(kb, GameAction.Restart) || Held(pad?.selectButton);
            PausePressed = Down(kb, GameAction.Pause) || Down(pad?.startButton);
#else
            // Legacy Input Manager fallback (unused: the project runs the Input System): default keys only.
            float move = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += 1f;
            _moveX = move;

            _jumpHeld = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            _hookHeld = Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.X) || Input.GetMouseButton(1);

            _jumpPressed |= Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            _attackPressed |= Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Z) || Input.GetMouseButtonDown(0);
            _slidePressed |= Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.LeftShift);
            _hookPressed |= Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.X) || Input.GetMouseButtonDown(1);
            _restartPressed |= Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Return);
            _restartHeld = Input.GetKey(KeyCode.R) || Input.GetKey(KeyCode.Return);
            PausePressed = Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        /// <summary>Dev-only (AutoPlay bot): replaces the device input of each fixed step when set. Null in normal play.</summary>
        internal static System.Func<InputFrame, InputFrame> DevFilter;

        /// <summary>Returns the input for one fixed step and clears the latched presses.</summary>
        public InputFrame Consume()
        {
            var frame = new InputFrame
            {
                MoveX = _moveX,
                MoveY = _moveY,
                JumpPressedByUp = _jumpPressed && _jumpByUpOnly,
                JumpHeld = _jumpHeld,
                HookHeld = _hookHeld || _hookPressed,
                JumpPressed = _jumpPressed,
                AttackPressed = _attackPressed,
                SlidePressed = _slidePressed,
                HookPressed = _hookPressed,
                RestartPressed = _restartPressed,
                RestartHeld = _restartHeld
            };
            ClearLatched();
            return DevFilter != null ? DevFilter(frame) : frame;
        }

        /// <summary>
        /// Drops latched presses and the pause press, e.g. while a menu is open, so a key used in a
        /// menu does not reach the first step of the run.
        /// </summary>
        public void ClearLatched()
        {
            _jumpPressed = false;
            _jumpByUpOnly = false;
            _attackPressed = false;
            _slidePressed = false;
            _hookPressed = false;
            _restartPressed = false;
            PausePressed = false;
        }

#if ENABLE_INPUT_SYSTEM
        private void ResolveBindings()
        {
            if (_bindings.Version == _bindingsVersion) return;
            _bindingsVersion = _bindings.Version;
            var actions = ControlBindings.Actions;
            for (int a = 0; a < actions.Length; a++)
            {
                int n = 0;
                for (int s = 0; s < ControlBindings.SlotsPerAction; s++)
                {
                    string name = _bindings.Get(actions[a], s);
                    if (name != null && Enum.TryParse(name, false, out Key key) && key != Key.None) _keys[a][n++] = key;
                }
                _keyCounts[a] = n;
            }
        }

        private bool Held(Keyboard kb, GameAction action)
        {
            if (kb == null) return false;
            var keys = _keys[(int)action];
            for (int i = 0, n = _keyCounts[(int)action]; i < n; i++)
                if (kb[keys[i]].isPressed) return true;
            return false;
        }

        private bool Down(Keyboard kb, GameAction action)
        {
            if (kb == null) return false;
            var keys = _keys[(int)action];
            for (int i = 0, n = _keyCounts[(int)action]; i < n; i++)
                if (kb[keys[i]].wasPressedThisFrame) return true;
            return false;
        }

        private static bool Held(ButtonControl control) => control != null && control.isPressed;

        private static bool Down(ButtonControl control) => control != null && control.wasPressedThisFrame;
#endif
    }
}
