using UnityEngine;
using UnityEngine.InputSystem;

namespace Ooda.Flight
{
    /// <summary>
    /// Gamepad and keyboard, read with the Input System. Bindings are made in code so there is no asset to keep in step.
    ///   Gamepad: left stick pitch and roll, bumpers rudder, triggers throttle, A war emergency power,
    ///            right stick look, Y camera, Start reset.
    ///   Keyboard: W/S or Up/Down pitch (S pulls), A/D or Left/Right roll, Q/E rudder, Shift/Ctrl throttle,
    ///            Space WEP, mouse look (hold right button in the chase view), C camera, Backspace reset.
    /// </summary>
    public class PlaneInput : MonoBehaviour
    {
        [Tooltip("Stick expo: 0 is linear, 1 is very soft around the centre.")]
        [Range(0, 1)] public float expo = 0.35f;
        [Tooltip("Throttle travel per second when the throttle buttons are held.")]
        public float throttleRate = 0.6f;
        [Range(0, 1)] public float startThrottle = 0.85f;
        public float mouseLookSensitivity = 0.15f;

        public float Pitch { get; private set; }
        public float Roll { get; private set; }
        public float Yaw { get; private set; }
        public float Throttle { get; set; }
        public bool Wep { get; private set; }
        /// <summary>Look input in degrees per second (stick) plus degrees this frame (mouse), x = yaw, y = pitch.</summary>
        public Vector2 LookRate { get; private set; }
        public Vector2 LookDelta { get; private set; }
        public bool LookHeld { get; private set; }
        public bool CameraPressed { get; private set; }
        public bool ResetPressed { get; private set; }
        public bool HelpPressed { get; private set; }

        InputAction pitch, roll, yaw, throttleUp, throttleDown, wep, lookStick, lookMouse, lookHold, cam, reset, help;

        void Awake()
        {
            Throttle = startThrottle;
            pitch = new InputAction("Pitch", InputActionType.Value);
            pitch.AddBinding("<Gamepad>/leftStick/y").WithProcessor("invert");
            pitch.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/s").With("Negative", "<Keyboard>/w");
            pitch.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/downArrow").With("Negative", "<Keyboard>/upArrow");

            roll = new InputAction("Roll", InputActionType.Value);
            roll.AddBinding("<Gamepad>/leftStick/x");
            roll.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/d").With("Negative", "<Keyboard>/a");
            roll.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/rightArrow").With("Negative", "<Keyboard>/leftArrow");

            yaw = new InputAction("Yaw", InputActionType.Value);
            yaw.AddCompositeBinding("1DAxis").With("Positive", "<Gamepad>/rightShoulder").With("Negative", "<Gamepad>/leftShoulder");
            yaw.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/e").With("Negative", "<Keyboard>/q");

            throttleUp = new InputAction("ThrottleUp", InputActionType.Value);
            throttleUp.AddBinding("<Gamepad>/rightTrigger");
            throttleUp.AddBinding("<Keyboard>/leftShift");
            throttleDown = new InputAction("ThrottleDown", InputActionType.Value);
            throttleDown.AddBinding("<Gamepad>/leftTrigger");
            throttleDown.AddBinding("<Keyboard>/leftCtrl");

            wep = new InputAction("WEP", InputActionType.Button);
            wep.AddBinding("<Gamepad>/buttonSouth");
            wep.AddBinding("<Keyboard>/space");

            lookStick = new InputAction("LookStick", InputActionType.Value);
            lookStick.AddBinding("<Gamepad>/rightStick");
            lookMouse = new InputAction("LookMouse", InputActionType.Value);
            lookMouse.AddBinding("<Mouse>/delta");
            lookHold = new InputAction("LookHold", InputActionType.Button);
            lookHold.AddBinding("<Mouse>/rightButton");

            cam = new InputAction("Camera", InputActionType.Button);
            cam.AddBinding("<Gamepad>/buttonNorth");
            cam.AddBinding("<Keyboard>/c");
            reset = new InputAction("Reset", InputActionType.Button);
            reset.AddBinding("<Gamepad>/start");
            reset.AddBinding("<Keyboard>/backspace");
            help = new InputAction("Help", InputActionType.Button);
            help.AddBinding("<Keyboard>/h");
            help.AddBinding("<Gamepad>/select");
        }

        InputAction[] All => new[] { pitch, roll, yaw, throttleUp, throttleDown, wep, lookStick, lookMouse, lookHold, cam, reset, help };
        void OnEnable() { foreach (var a in All) a.Enable(); }
        void OnDisable() { foreach (var a in All) a.Disable(); }
        void OnDestroy() { foreach (var a in All) a.Dispose(); }

        float Shape(float v)
        {
            v = Mathf.Clamp(v, -1, 1);
            return (1 - expo) * v + expo * v * v * v;
        }

        void Update()
        {
            Pitch = Shape(pitch.ReadValue<float>());
            Roll = Shape(roll.ReadValue<float>());
            Yaw = Mathf.Clamp(yaw.ReadValue<float>(), -1, 1);
            Throttle = Mathf.Clamp01(Throttle + (throttleUp.ReadValue<float>() - throttleDown.ReadValue<float>()) * throttleRate * Time.unscaledDeltaTime);
            Wep = wep.IsPressed();
            LookRate = lookStick.ReadValue<Vector2>() * 180f;
            LookDelta = lookMouse.ReadValue<Vector2>() * mouseLookSensitivity;
            LookHeld = lookHold.IsPressed();
            CameraPressed = cam.WasPressedThisFrame();
            ResetPressed = reset.WasPressedThisFrame();
            HelpPressed = help.WasPressedThisFrame();
        }
    }
}
