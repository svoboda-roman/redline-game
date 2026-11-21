using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Character select (main menu > CHARACTER): the Ronin with the katana or the Reaper with the scythe. The choice is
    /// saved in the settings file (<see cref="SettingsData.Character"/>); a new run applies its weapon tuning
    /// (<see cref="Characters.Apply"/>) and the actor views are rebuilt so the right baked sheet is drawn.
    /// </summary>
    public sealed partial class RedlineGame
    {
        private static readonly string[] CharacterNames = { "RONIN", "REAPER" };
        private static readonly string[] CharacterWeapons = { "KATANA", "SCYTHE" };
        private static readonly string[] CharacterBlurbs =
        {
            "KATANA  -  FAST, BALANCED STRIKES",
            "SCYTHE  -  LONG REACH, SLOW HEAVY SWINGS, +25% DAMAGE, 50 HP, +1 HP PER HIT",
        };

        private MenuScreen _characterMenu;

        /// <summary>The character chosen in the settings.</summary>
        public static PlayerCharacter SelectedCharacter =>
            Characters.FromIndex(GameSettings.Current != null ? GameSettings.Current.Character : 0);

        /// <summary>Evidence captures only: REDLINE_CHARACTER=0|1 forces the character for this session (not saved).</summary>
        private static void ApplyCharacterOverride()
        {
            string v = System.Environment.GetEnvironmentVariable("REDLINE_CHARACTER");
            if (!string.IsNullOrEmpty(v) && int.TryParse(v, out int i) && GameSettings.Current != null)
                GameSettings.Current.Character = (int)Characters.FromIndex(i);
        }

        private void BuildCharacterMenu()
        {

            _characterMenu = gameObject.AddComponent<MenuScreen>();
            _characterMenu.Init("CharacterMenu", 90f, "CHARACTER", true);
            for (int i = 0; i < Characters.Count; i++)
            {
                int k = i;
                _characterMenu.AddItem(CharacterNames[i], () => PickCharacter(Characters.FromIndex(k)));
            }
            _characterMenu.AddItem("BACK", CloseCharacterSelect);
            _characterMenu.Cancelled = CloseCharacterSelect;
            _characterMenu.SelectionChanged = i =>
                _characterMenu.SetNotice(i >= 0 && i < Characters.Count ? CharacterBlurbs[i] : "");
        }

        private void OpenCharacterSelect()
        {
            _mainMenu.Hide();
            RefreshCharacterLabels();
            _characterMenu.SetNotice(CharacterBlurbs[(int)SelectedCharacter]);
            _characterMenu.Show();
            SetFlow(_flow);
        }

        private void CloseCharacterSelect()
        {
            _characterMenu.Hide();
            _mainMenu.Show();
            SetFlow(_flow);
        }

        private void RefreshCharacterLabels()
        {
            for (int i = 0; i < Characters.Count; i++)
            {
                string tail = (int)SelectedCharacter == i ? "SELECTED" : CharacterWeapons[i];
                _characterMenu.SetItemLabel(i, CharacterNames[i].PadRight(10) + tail);
            }
        }

        private void PickCharacter(PlayerCharacter character)
        {
            if (character != SelectedCharacter)
            {
                GameSettings.Current.Character = (int)character;
                GameSettings.Save();
                // The menu backdrop runs the live level: rebuild the actor views so the new character is drawn.
                if (_world != null) BuildViews();
            }
            RefreshCharacterLabels();
            _characterMenu.SetNotice(CharacterBlurbs[(int)character]);
            // ENDLESS: after the character comes the map (a level or RANDOM MAP); picking it starts the run.
            _characterMenu.Hide();
            OpenLevelSelect();
        }
    }
}
