using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Enumerations;
using BrilliantSkies.Core.Help;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Returns;
using BrilliantSkies.Core.Returns.Interfaces;
using BrilliantSkies.Core.Returns.Positions;
using BrilliantSkies.Core.Returns.Rotation;
using BrilliantSkies.Core.Unity;
using BrilliantSkies.Effects.Cameras;
using BrilliantSkies.Environments;
using BrilliantSkies.Environments.Skies.Fogging;
using BrilliantSkies.FromTheDepths.Game.UserInterfaces;
using BrilliantSkies.Ftd.Avatar.Movement;
using BrilliantSkies.Modding;
using BrilliantSkies.PlayerProfiles;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace FTDFilmTheDepthsMod
{
    public class Main : GamePlugin
    {
        public static cCameraControl cameraControl = null; // Cache camera control.
        // Track change in rotation of camera subjects.
        public static Quaternion lastRotation = Quaternion.identity;
        public static IRotationReturn rotation = null;

        public string name { get; } = "Film The Depths";
        public Version version { get; } = new Version(0, 1, 0);

        public void OnLoad()
        {
            Harmony harmony = new Harmony("cappycot.filmthedepths");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            AdvLogger.LogInfo("Film The Depths is ready.");
        }

        public void OnSave()
        {
        }
    }

    /// <summary>
    /// Cache cCameraControl object.
    /// </summary>
    [HarmonyPatch(typeof(cCameraControl), "Start")]
    class cCameraControl_Start_Patch
    {
        static void Postfix(cCameraControl __instance)
        {
            Main.cameraControl = __instance;
        }
    }

    /// <summary>
    /// Record user intent to move mouse.
    /// </summary>
    [HarmonyPatch(typeof(MouseLook), "Update")]
    class MouseLook_Update_Patch
    {
        public static float xIntent = 0f;
        public static float yIntent = 0f;

        static bool Prefix(MouseLook __instance, MViewAndControl ____options, float ___minimumY, float ___maximumY)
        {
            cCameraControl ccc = Main.cameraControl;
            FocusCameraSettings fcs = ccc?._externalCameraFocusSettings;
            // Note: If ccc or fcs is null, first condition fails.
            if (fcs?.PositionOfFocus != null && fcs.PositionOfFocus.Valid &&
                (ccc.CameraState == enumCameraState.detached ||
                ccc.CameraState == enumCameraState.unparented) &&
                // As of time of writing, cCameraControl sets _mouseLookY
                // as the sole MouseLook handling both MouseXAndY axes.
                __instance.axes == enumRotationAxes.MouseXAndY &&
                !(FleetControlGUI.Instance.MenuActive && ____options.TopDownViewLocked))
            {
                if (Get.UserInput.MouseLookEnabled && __instance.DisableForAFewFrames.Since > 0.1f)
                {
                    // Record mouse intent to move.
                    float num = (Time.timeScale == 0f) ? 1f : Mathf.Clamp(Time.timeScale, 0f, 1f);
                    float newXIntent = Input.GetAxis("Mouse X") * __instance.sensitivityX * num;
                    if (Math.Abs(newXIntent) > Math.Abs(xIntent))
                        xIntent = newXIntent;
                    float newYIntent = Input.GetAxis("Mouse Y") * __instance.sensitivityY * num *
                        ____options.GetMouseInvertSign();
                    if (Math.Abs(newYIntent) > Math.Abs(yIntent))
                        yIntent = newYIntent;
                    Vector2 v = ____options.GetMouseSpeed();
                    __instance.sensitivityX = 15f * v.x;
                    __instance.sensitivityY = 15f * v.y;
                }
                return false;
            }
            else
                return true;
        }
    }

    /// <summary>
    /// Get position and rotation return of camera subject.
    /// </summary>
    [HarmonyPatch(typeof(cCameraControl), "FocusExternalCameraOnThis")]
    class cCameraControl_FocusExternalCameraOnThis_Patch
    {
        static void Postfix(cCameraControl __instance, FocusCameraSettings settings)
        {
            cCameraControl_IEFocusExternalCameraOnThis_Patch.xRotVel = 0f;
            cCameraControl_IEFocusExternalCameraOnThis_Patch.yRotVel = 0f;
            Main.rotation = null;
            IPositionReturn pof = settings.PositionOfFocus;
            switch (pof)
            {
                case PositionReturnBlock prb: // FocusCameraSettings were directed to some construct's block.
                    FieldInfo fib = typeof(PositionReturnBlock).GetField("_b", BindingFlags.NonPublic | BindingFlags.Instance);
                    IBlockForReturns b = (IBlockForReturns)fib.GetValue(prb);
                    if (b is Block block)
                    {
                        IAllConstructBlock acb = block.GetConstructableOrSubConstructable();
                        if (acb is AllConstruct ac)
                            Main.rotation = new RotationReturnConstruct(ac);
                        else // Should not be the case.
                            Main.rotation = new RotationReturnBlock(b);
                        Main.lastRotation = Main.rotation.Rotation;
                    }
                    break;
                case PositionReturnForce prf: // FocusCameraSettings were created from the strategic view.
                    FieldInfo fif = typeof(PositionReturnForce).GetField("_f", BindingFlags.NonPublic | BindingFlags.Instance);
                    Force f = (Force)fif.GetValue(prf);
                    if (f.C != null)
                    {
                        // Center camera on CoM if coming from strategic map.
                        PositionAndRotationReturnConstruct yeah = new PositionAndRotationReturnConstruct(f.C, PositionReturnConstructReferenceSelection.CenterOfMass);
                        settings.PositionOfFocus = yeah;
                        Main.rotation = yeah;
                        Main.lastRotation = yeah.Rotation;
                    }
                    else
                    {
                        // This produces results hysterically bad for filming.
                        // Not sure if this case is possible.
                        Main.rotation = new RotationReturnForce(f);
                        Main.lastRotation = Main.rotation.Rotation;
                    }
                    break;
                case PositionReturnTransform prt: // FocusCameraSettings were directed to a projectile.
                    FieldInfo fit = typeof(PositionReturnTransform).GetField("_t", BindingFlags.NonPublic | BindingFlags.Instance);
                    Transform t = (Transform)fit.GetValue(prt);
                    Main.rotation = new RotationReturnTransform(t);
                    Main.lastRotation = Main.rotation.Rotation;
                    break;
                default: // Other focus subjects won't be tracked rotation-wise. (Don't think there are any others.)
                    break;
            }
        }
    }

    /// <summary>
    /// Cancel external camera focus.
    /// </summary>
    [HarmonyPatch(typeof(cCameraControl), "CancelExternalCameraFocus")]
    class cCameraControl_CancelExternalCameraFocus_Patch
    {
        static void Postfix(cCameraControl __instance, MouseLook ____mouseLookX, MouseLook ____mouseLookY)
        {
            MouseLook_Update_Patch.xIntent = 0f;
            MouseLook_Update_Patch.yIntent = 0f;
            cCameraControl_IEFocusExternalCameraOnThis_Patch.xRotVel = 0f;
            cCameraControl_IEFocusExternalCameraOnThis_Patch.yRotVel = 0f;
            Main.rotation = null;
        }
    }

    /// <summary>
    /// Move external camera when focusing on a subject.
    /// </summary>
    [HarmonyPatch(typeof(cCameraControl), "IEFocusExternalCameraOnThis")]
    class cCameraControl_IEFocusExternalCameraOnThis_Patch
    {
        public static float xRotVel = 0f;
        public static float yRotVel = 0f;
        // private static float lastTime = 1.0f;

        static bool Prefix(cCameraControl __instance, KeyDef ____zoomIn, KeyDef ____zoomOut, MViewAndControl ____options, MouseLook ____mouseLookY)
        {
            /*if (lastTime != Time.timeScale)
            {
                lastTime = Time.timeScale;
                AdvLogger.LogInfo("Time.timeScale changed to " + lastTime);
            }*/
            Transform cam = __instance.Cam;
            FocusCameraSettings fcs = __instance._externalCameraFocusSettings;
            if (fcs?.PositionOfFocus == null || !fcs.PositionOfFocus.Valid ||
                __instance.CameraState != enumCameraState.detached &&
                __instance.CameraState != enumCameraState.unparented)
                __instance.CancelExternalCameraFocus();
            else if (____mouseLookY.axes == enumRotationAxes.MouseXAndY)
            {
                // Check if user is doing top-down strategic map and don't rotate if so.
                bool topdown = FleetControlGUI.Instance.MenuActive && ____options.TopDownViewLocked;
                if (!topdown)
                {
                    // Record rotational change if any.
                    // Unity euler angles are Z, X, Y.
                    float xChange = 0f;
                    if (Main.rotation != null && Main.rotation.Valid)
                    {
                        Quaternion rot = Main.rotation.Rotation;
                        xChange = rot.eulerAngles.y - Main.lastRotation.eulerAngles.y;
                        Main.lastRotation = rot;
                    }
                    else
                        Main.rotation = null;
                    // TODO: Smoothing speed setting.
                    xRotVel = Mathf.Lerp(xRotVel, MouseLook_Update_Patch.xIntent, 0.02f);
                    yRotVel = Mathf.Lerp(yRotVel, MouseLook_Update_Patch.yIntent, 0.02f);
                    ____mouseLookY.rotationX = ____mouseLookY.transform.localEulerAngles.y + xChange + xRotVel;
                    ____mouseLookY.rotationY += yRotVel;
                    // The numbers -89 and 89 are hard-coded/unmodified values from vanilla.
                    ____mouseLookY.rotationY = Mathf.Clamp(____mouseLookY.rotationY, -89f, 89f);
                    ____mouseLookY.transform.localEulerAngles = new Vector3(-____mouseLookY.rotationY, ____mouseLookY.rotationX, 0f);
                    // Since DoingStrategicFocus inhibits zoom in/out, switch to another method.
                    fcs.DistanceMethod = fcs.DistanceMethod == DistanceChangeMethod.DoingStrategicFocus ?
                        DistanceChangeMethod.MaintainCurrentFocusDistance : fcs.DistanceMethod;
                }
                else // Disallow focus camera zoom when in strategic view.
                    fcs.DistanceMethod = DistanceChangeMethod.DoingStrategicFocus;
                // Done with mouse input for this frame, so remove it.
                MouseLook_Update_Patch.xIntent = MouseLook_Update_Patch.yIntent = 0f;

                // Copied from vanilla function.
                Vector3 focus = fcs.PositionOfFocus.Position;
                Vector3 forward = cam.forward;
                Vector3 b = focus - forward * fcs.Distance;

                // Don't move the camera's altitude if strategic view is already setting it.
                if (topdown)
                    b.y = cam.position.y;
                else
                {
                    // TODO: Clamp altitude of b based on terrain.
                }

                __instance.Cam.position = b; // Removed lerp.
                // Again copied from vanilla function.
                float axis = Input.GetAxis("Mouse ScrollWheel");
                if (____zoomIn.IsKey(KeyInputEventType.Held, ModifierAllows.AllowShift)
                    || axis > 0f)
                {
                    // TODO: Possibly check keybinding instead?
                    int num = OptionsListener.ShiftHeld ? 4 : 1;
                    ____options.FocusCameraDistance -= 0.1f * (float)num;
                    ____options.FocusCameraDistance *= 1f - 0.02f * (float)num;
                    ____options.FocusCameraDistance = Clamping.Clamp(____options.FocusCameraDistance, 1f, 1000f);
                }
                else if (____zoomOut.IsKey(KeyInputEventType.Held, ModifierAllows.AllowShift)
                  || axis < 0f)
                {
                    int num = OptionsListener.ShiftHeld ? 4 : 1;
                    ____options.FocusCameraDistance += 0.1f * (float)num;
                    ____options.FocusCameraDistance *= 1f + 0.02f * (float)num;
                    ____options.FocusCameraDistance = Clamping.Clamp(____options.FocusCameraDistance, 1f, 1000f);
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Disable camera water splash unless in first person.
    /// </summary>
    [HarmonyPatch(typeof(Fog), "SetBelowWater")]
    class Fog_SetBelowWater_Patch
    {
        static void Prefix(IEnvInputs inputs)
        {
            bool splash = Main.cameraControl != null &&
                Main.cameraControl.CameraState == enumCameraState.firstPerson;
            inputs.CamEffects.IsPlaySplashSound = splash;
        }
    }
}
