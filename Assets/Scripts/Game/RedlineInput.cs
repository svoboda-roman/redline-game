using Redline.Core;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Redline.Game
{
    /// <summary>
    /// Reads keyboard and gamepad every rendered frame and hands the simulation one
    /// <see cref="InputFrame"/> per fixed step. Button presses are latched until a step
    /// consumes them, so a press is never lost when a frame runs no step.
    /// </summary>
    /// <example>
    /// <code>
    /// input.Poll();                         // once per Update
    /// sim.Step(input.Consume(), dt);        // once per fixed step
    /// </code>
    /// </example>
    /// <remarks>
    /// Keyboard: A/D or arrows move, Space/W/Up jump, J/Z or left mouse attack,
    /// K/X or right mouse hold to hook, S/Down/Shift slide, R or Enter restart.
    /// Gamepad: stick or d-pad move, South jump, West attack, right shoulder/trigger hook,
    /// East or left trigger slide, Start restart.
    /// </remarks>
    public sealed class RedlineInput
    {
        private float _moveX;
        private bool _jumpHeld;
        private bool _hookHeld;
        private bool _jumpPressed;
        private bool _attackPressed;
        private bool _slidePressed;
        private bool _hookPressed;
        private bool _restartPressed;

        /// <summary>Samples devices. Call once per rendered frame.</summary>
        public void Poll()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            float move = 0f;
            if (Held(kb?.aKey) || Held(kb?.leftArrowKey)) move -= 1f;
            if (Held(kb?.dKey) || Held(kb?.rightArrowKey)) move += 1f;
            if (pad != null)
            {
                float stick = pad.leftStick.ReadValue().x;
                if (Mathf.Abs(stick) > 0.3f) move = stick;
                if (pad.dpad.left.isPressed) move = -1f;
                if (pad.dpad.right.isPressed) move = 1f;
            }
            _moveX = Mathf.Clamp(move, -1f, 1f);

            _jumpHeld = Held(kb?.spaceKey) || Held(kb?.wKey) || Held(kb?.upArrowKey) || Held(pad?.buttonSouth);
            _hookHeld = Held(kb?.kKey) || Held(kb?.xKey) || Held(mouse?.rightButton)
                        || Held(pad?.rightShoulder) || Held(pad?.rightTrigger);

            _jumpPressed |= Down(kb?.spaceKey) || Down(kb?.wKey) || Down(kb?.upArrowKey) || Down(pad?.buttonSouth);
            _attackPressed |= Down(kb?.jKey) || Down(kb?.zKey) || Down(mouse?.leftButton) || Down(pad?.buttonWest);
            _slidePressed |= Down(kb?.sKey) || Down(kb?.downArrowKey) || Down(kb?.leftShiftKey)
                             || Down(pad?.buttonEast) || Down(pad?.leftTrigger);
            _hookPressed |= Down(kb?.kKey) || Down(kb?.xKey) || Down(mouse?.rightButton)
                            || Down(pad?.rightShoulder) || Down(pad?.rightTrigger);
            _restartPressed |= Down(kb?.rKey) || Down(kb?.enterKey) || Down(pad?.startButton);
#else
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
#endif
        }

        /// <summary>Returns the input for one fixed step and clears the latched presses.</summary>
        public InputFrame Consume()
        {
            var frame = new InputFrame
            {
                MoveX = _moveX,
                JumpHeld = _jumpHeld,
                HookHeld = _hookHeld || _hookPressed,
                JumpPressed = _jumpPressed,
                AttackPressed = _attackPressed,
                SlidePressed = _slidePressed,
                HookPressed = _hookPressed,
                RestartPressed = _restartPressed
            };
            _jumpPressed = false;
            _attackPressed = false;
            _slidePressed = false;
            _hookPressed = false;
            _restartPressed = false;
            return frame;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool Held(ButtonControl control) => control != null && control.isPressed;

        private static bool Down(ButtonControl control) => control != null && control.wasPressedThisFrame;
#endif
    }
}
