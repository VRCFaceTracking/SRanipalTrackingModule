using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ViveSR;
using ViveSR.anipal;
using ViveSR.anipal.Eye;
using ViveSR.anipal.Lip;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Types;

namespace SRanipalExtTrackingInterface
{
    public class SRanipalExtTrackingInterface : ExtTrackingModule
    {
        LipData_v2 lipData = default;
        EyeData_v2 eyeData = default;
        private static bool eyeEnabled = false, 
                            lipEnabled = false, 
                            isViveProEye = false,
                            isWireless = false;
        private static Error eyeError = Error.UNDEFINED;
        private static Error lipError = Error.UNDEFINED;

        internal static Process? _process;
        internal static IntPtr _processHandle;
        internal static IntPtr _offset;
        
        private static byte[] eyeImageCache, lipImageCache;
        
        // Kernel32 SetDllDirectory
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);
        
        private static bool Attach()
        {
            var processes = Process.GetProcessesByName("sr_runtime");
            if (processes.Length <= 0) return false;
            _process = processes[0];
            _processHandle =
                Utils.OpenProcess(Utils.PROCESS_VM_READ,
                    false, _process.Id);
            return true;
        }

        private static byte[] ReadMemory(IntPtr offset, ref byte[] buf) {
            var bytesRead = 0;
            var size = buf.Length;
            
            Utils.ReadProcessMemory((int) _processHandle, offset, buf, size, ref bytesRead);

            return bytesRead != size ? null : buf;
        }

        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            System.Reflection.Assembly a = System.Reflection.Assembly.GetExecutingAssembly();
            var hmdStream = a.GetManifestResourceStream("SRanipalExtTrackingModule.Assets.vive_hmd.png");
            var lipStream = a.GetManifestResourceStream("SRanipalExtTrackingModule.Assets.vive_face_tracker.png");

            // Look for SRanipal assemblies here. Placeholder for unmanaged assemblies not being embedded in the dll.
            var currentDllDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            
            // Get the directory of the sr_runtime.exe program from our start menu shortcut. This is where the SRanipal dlls are located.
            var srInstallDir = (string) Registry.LocalMachine.OpenSubKey(@"Software\VIVE\SRWorks\SRanipal")?.GetValue("ModuleFileName");

            // Dang you SRanipal
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var srLogsDirectory = Path.Combine(localAppData + @"Low\HTC Corporation\SR_Logs\SRAnipal_Logs");
            
            // Get logs that should be yeeted.
            string[] srLogFiles = Directory.GetFiles(srLogsDirectory);
        
            foreach (string logFile in srLogFiles)
            {
                try {
                    using (var stream = File.Open(logFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    {
                        Logger.LogDebug($"Clearing \"{logFile}\"");
                        stream.SetLength(0);
                        stream.Close();
                    }
                }
                catch {
                    Logger.LogWarning($"Failed to delete log file \"{logFile}\"");
                }
            }

            if (srInstallDir == null)
            {
                Logger.LogError("Bruh, SRanipal not installed. Assuming default path");
                srInstallDir = "C:\\Program Files\\VIVE\\SRanipal\\sr_runtime.exe";
            }
            
            // Get the currently installed sr_runtime version. If it's above 1.3.6.* then we use ModuleLibs\\New
            var srRuntimeVer = "1.3.1.1";   // We'll assume 1.3.1.1 if we can't find the version.
            try
            {
                srRuntimeVer = FileVersionInfo.GetVersionInfo(srInstallDir).FileVersion;
            }
            catch
            {
                Logger.LogDebug("Smh you've got a bad install of SRanipal. Because you're like 97% likely to complain in the discord about this, I'll just assume you're using 1.3.1.1");
                Logger.LogDebug("I swear to god if you complain about this and have also fucked around with the sranipal install dir and have a version higher than 1.3.6.* I will ban you faster than my father dropped me as a child do you understand");
            }
            
            Logger.LogInformation($"SRanipalExtTrackingModule: SRanipal version: {srRuntimeVer}");
            
            SetDllDirectory(currentDllDirectory + "\\ModuleLibs\\" + (srRuntimeVer.StartsWith("1.3.6") ? "New" : "Old"));

            SRanipal_API.InitialRuntime(); // hack to unblock sranipal!!!

            eyeEnabled = InitTracker(SRanipal_Eye_v2.ANIPAL_TYPE_EYE_V2, "Eye");
            lipEnabled = InitTracker(SRanipal_Lip_v2.ANIPAL_TYPE_LIP_V2, "Lip");

            if (eyeEnabled && Utils.HasAdmin)
            {
                var found = false;
                int tries = 0;
                while (!found && tries < 15)
                {
                    tries++;
                    found = Attach();
                    Thread.Sleep(250);
                }

                if (found)
                {
                    // Find the EyeCameraDevice.dll module inside sr_runtime, get it's offset and add hex 19190 to it for the image stream.
                    foreach (ProcessModule module in _process.Modules)
                        if (module.ModuleName == "EyeCameraDevice.dll")
                        {
                            _offset = module.BaseAddress; 
                            
                            switch (_process.MainModule?.FileVersionInfo.FileVersion)
                            {
                                case "1.3.2.0":
                                    _offset += 0x19190;
                                    UnifiedTracking.EyeImageData.SupportsImage = true;
                                    break;
                                case "1.3.1.1":
                                    _offset += 0x19100;
                                    UnifiedTracking.EyeImageData.SupportsImage = true;
                                    break;
                                default:
                                    UnifiedTracking.EyeImageData.SupportsImage = false;
                                    break;
                            }
                        }
                            
                    UnifiedTracking.EyeImageData.ImageSize = (200, 100);
                    UnifiedTracking.EyeImageData.ImageData = new byte[200 * 100 * 4];
                    eyeImageCache = new byte[200 * 100];
                }
            }
            
            if (lipEnabled)
            {
                UnifiedTracking.LipImageData.SupportsImage = true;
                UnifiedTracking.LipImageData.ImageSize = (SRanipal_Lip_v2.ImageWidth, SRanipal_Lip_v2.ImageHeight);
                lipData.image = Marshal.AllocCoTaskMem(UnifiedTracking.LipImageData.ImageSize.x *
                                                       UnifiedTracking.LipImageData.ImageSize.x);

                UnifiedTracking.LipImageData.ImageData = new byte[SRanipal_Lip_v2.ImageWidth * SRanipal_Lip_v2.ImageHeight * 4];
                lipImageCache = new byte[SRanipal_Lip_v2.ImageWidth * SRanipal_Lip_v2.ImageHeight];
            }

            ModuleInformation = new ModuleMetadata()
            {
                Name = "VIVE SRanipal",
            };
            List<Stream> streams = new List<Stream>();
            if (eyeEnabled)
                streams.Add(hmdStream);
            if (lipEnabled)
                streams.Add(lipStream);
            ModuleInformation.StaticImages = streams;

            isViveProEye = SRanipal_Eye_API.IsViveProEye();

            return (eyeAvailable && eyeEnabled, expressionAvailable && lipEnabled);
        }

        private bool InitTracker(int anipalType, string name)
        {
            Logger.LogInformation($"Initializing {name}...");
            var error = SRanipal_API.Initial(anipalType, IntPtr.Zero);

            handler:
            switch (error)
            {
                case Error.FOXIP_SO: // wireless issue
                    Logger.LogInformation("Vive wireless detected. Forcing initialization...");
                    while (error == Error.FOXIP_SO)
                        error = SRanipal_API.Initial(anipalType, IntPtr.Zero);
                    goto handler;
                case Error.WORK:
                    Logger.LogInformation($"{name} successfully started!");
                    return true;
                default:
                    break;
            }
            Logger.LogInformation($"{name} failed to initialize: {error}");
            return false;
        }
        
        public override void Teardown()
        {
            SRanipal_API.ReleaseRuntime();
        }

        public override void Update()
        {
            Thread.Sleep(10);

            if (Status != ModuleState.Active)
                return;
            if (lipEnabled && !UpdateMouth())
            {
                Logger.LogError("An error has occured when updating tracking. Reinitializing needed runtimes.");
                SRanipal_API.InitialRuntime();
                InitTracker(SRanipal_Lip_v2.ANIPAL_TYPE_LIP_V2, "Lip");
            }
            if (eyeEnabled && !UpdateEye())
            {
                Logger.LogError("An error has occured when updating tracking. Reinitializing needed runtimes.");
                SRanipal_API.InitialRuntime();
                InitTracker(SRanipal_Eye_v2.ANIPAL_TYPE_EYE_V2, "Eye");
            }
        }

        private bool UpdateEye()
        {
            eyeError = SRanipal_Eye_API.GetEyeData_v2(ref eyeData);
            if (eyeError != Error.WORK) return false;

            UpdateEyeParameters(ref UnifiedTracking.Data.Eye, eyeData.verbose_data);
            UpdateEyeExpressions(ref UnifiedTracking.Data.Shapes, eyeData.expression_data);

            if (_processHandle == IntPtr.Zero || !UnifiedTracking.EyeImageData.SupportsImage) 
                return true;
            
            // Read 20000 image bytes from the predefined offset. 10000 bytes per eye.
            var imageBytes = ReadMemory(_offset, ref eyeImageCache);
            
            // Concatenate the two images side by side instead of one after the other
            byte[] leftEye = new byte[10000];
            Array.Copy(imageBytes, 0, leftEye, 0, 10000);
            byte[] rightEye = new byte[10000];
            Array.Copy(imageBytes, 10000, rightEye, 0, 10000);
            
            for (var i = 0; i < 100; i++)   // 100 lines of 200 bytes
            {
                // Add 100 bytes from the left eye to the left side of the image
                int leftIndex = i * 100 * 2;
                Array.Copy(leftEye,i*100, imageBytes, leftIndex, 100);

                // Add 100 bytes from the right eye to the right side of the image
                Array.Copy(rightEye, i*100, imageBytes, leftIndex + 100, 100);
            }
            
            for (int y = 0; y < 100; y++)
            {
                for (int x = 0; x < 200; x++)
                {
                    byte grayscaleValue = imageBytes[y * 200 + x];

                    // Set the R, G, B, and A channels to the grayscale value
                    int index = (y * 200 + x) * 4;
                    UnifiedTracking.EyeImageData.ImageData[index + 0] = grayscaleValue; // R
                    UnifiedTracking.EyeImageData.ImageData[index + 1] = grayscaleValue; // G
                    UnifiedTracking.EyeImageData.ImageData[index + 2] = grayscaleValue; // B
                    UnifiedTracking.EyeImageData.ImageData[index + 3] = 255; // A (fully opaque)
                }
            }

            return true;
        }

        private static Vector3 GetConvergenceAngleOffset(VerboseData external)
        {
            var leftComp = Math.PI / 2 + Math.Asin(external.left.gaze_direction_normalized.FlipXCoordinates().x);
            var rightComp = Math.PI / 2 - Math.Asin(external.right.gaze_direction_normalized.FlipXCoordinates().x);

            var dynIPD_mm = external.left.gaze_origin_mm.x - external.right.gaze_origin_mm.x;

            if (leftComp + rightComp >= Math.PI)
                return new Vector3(0,0,0);

            var rightSide_mm = Math.Sin(rightComp) * dynIPD_mm / Math.Sin(Math.PI - leftComp - rightComp);
            var leftSide_mm = Math.Sin(leftComp) * dynIPD_mm / Math.Sin(Math.PI - rightComp - leftComp);

            var convergenceDistance_mm = (leftSide_mm/2f) + (rightSide_mm/2f);


            if (external.combined.eye_data.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
                return new Vector3((float)Math.Atan((dynIPD_mm / 2f) / convergenceDistance_mm), 0, 0);
            return Vector3.zero;
        }

        private void UpdateEyeParameters(ref UnifiedEyeData data, VerboseData external)
        {
            if (external.left.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_EYE_OPENNESS_VALIDITY))
                data.Left.Openness = external.left.eye_openness;
            if (external.right.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_EYE_OPENNESS_VALIDITY))
                data.Right.Openness = external.right.eye_openness;

            if (external.left.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_PUPIL_DIAMETER_VALIDITY))
                data.Left.PupilDiameter_MM = external.left.pupil_diameter_mm;
            if (external.right.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_PUPIL_DIAMETER_VALIDITY))
                data.Right.PupilDiameter_MM = external.right.pupil_diameter_mm;
            
            if (isViveProEye)
            {
                if (external.left.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
                    data.Left.Gaze = external.left.gaze_direction_normalized.FlipXCoordinates();
                if (external.right.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
                    data.Right.Gaze = external.right.gaze_direction_normalized.FlipXCoordinates();
                return;
            }
            
            // Fix for Focus 3 / Droolon F1 gaze tracking. For some reason convergence data isn't available from combined set so we will calculate it from the two gaze vectors.
            if (external.left.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY) && external.right.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
            {
                Vector3 gaze_direction_normalized = (external.left.gaze_direction_normalized.FlipXCoordinates()/2f) + (external.right.gaze_direction_normalized.FlipXCoordinates()/2f);
                //Vector3 convergenceOffset = GetConvergenceAngleOffset(external);
                data.Left.Gaze = gaze_direction_normalized;
                data.Right.Gaze = gaze_direction_normalized;
            }
        }

        private void UpdateEyeExpressions(ref UnifiedExpressionShape[] data, EyeExpression external)
        {
            data[(int)UnifiedExpressions.EyeWideLeft].Weight = external.left.eye_wide;
            data[(int)UnifiedExpressions.EyeWideRight].Weight = external.right.eye_wide;

            data[(int)UnifiedExpressions.EyeSquintLeft].Weight = external.left.eye_squeeze;
            data[(int)UnifiedExpressions.EyeSquintRight].Weight = external.right.eye_squeeze;

            // Emulator expressions for Unified Expressions. These are essentially already baked into Legacy eye expressions (SRanipal)
            data[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = external.left.eye_wide;
            data[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = external.left.eye_wide;

            data[(int)UnifiedExpressions.BrowInnerUpRight].Weight = external.right.eye_wide;
            data[(int)UnifiedExpressions.BrowOuterUpRight].Weight = external.right.eye_wide;

            data[(int)UnifiedExpressions.BrowPinchLeft].Weight = external.left.eye_squeeze;
            data[(int)UnifiedExpressions.BrowLowererLeft].Weight = external.left.eye_squeeze;

            data[(int)UnifiedExpressions.BrowPinchRight].Weight = external.right.eye_squeeze;
            data[(int)UnifiedExpressions.BrowLowererRight].Weight = external.right.eye_squeeze;
        }

        private bool UpdateMouth()
        {
            lipError = SRanipal_Lip_API.GetLipData_v2(ref lipData);
            if (lipError != Error.WORK)
                return false;
            UpdateMouthExpressions(ref UnifiedTracking.Data, lipData.prediction_data);

            if (lipData.image == IntPtr.Zero || !UnifiedTracking.LipImageData.SupportsImage) 
                return true;

            Marshal.Copy(lipData.image, lipImageCache, 0, UnifiedTracking.LipImageData.ImageSize.x *
            UnifiedTracking.LipImageData.ImageSize.y);
            
            for (int y = 0; y < 400; y++)
            {
                for (int x = 0; x < 800; x++)
                {
                    byte grayscaleValue = lipImageCache[y * 800 + x];

                    // Set the R, G, B, and A channels to the grayscale value
                    int index = (y * 800 + x) * 4;
                    UnifiedTracking.LipImageData.ImageData[index + 0] = grayscaleValue; // R
                    UnifiedTracking.LipImageData.ImageData[index + 1] = grayscaleValue; // G
                    UnifiedTracking.LipImageData.ImageData[index + 2] = grayscaleValue; // B
                    UnifiedTracking.LipImageData.ImageData[index + 3] = 255; // A (fully opaque)
                }
            }

            return true;
        }

        private void UpdateMouthExpressions(ref UnifiedTrackingData data, PredictionData_v2 external)
        {
            unsafe
            {
                #region Direct Jaw

                data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = external.blend_shape_weight[(int)LipShape_v2.JawOpen] + external.blend_shape_weight[(int)LipShape_v2.MouthApeShape];
                data.Shapes[(int)UnifiedExpressions.JawLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.JawLeft];
                data.Shapes[(int)UnifiedExpressions.JawRight].Weight = external.blend_shape_weight[(int)LipShape_v2.JawRight];
                data.Shapes[(int)UnifiedExpressions.JawForward].Weight = external.blend_shape_weight[(int)LipShape_v2.JawForward];
                data.Shapes[(int)UnifiedExpressions.MouthClosed].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthApeShape];

                #endregion

                #region Direct Mouth and Lip

                // These shapes have overturns subtracting from them, as we are expecting the new standard to have Upper Up / Lower Down baked into the funneller shapes below these.
                data.Shapes[(int)UnifiedExpressions.MouthUpperUpRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperUpRight] - external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];
                data.Shapes[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperUpRight] - external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];
                data.Shapes[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperUpLeft] - external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];
                data.Shapes[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperUpLeft] - external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];

                data.Shapes[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerDownLeft] - external.blend_shape_weight[(int)LipShape_v2.MouthLowerOverturn];
                data.Shapes[(int)UnifiedExpressions.MouthLowerDownRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerDownRight] - external.blend_shape_weight[(int)LipShape_v2.MouthLowerOverturn];

                data.Shapes[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthPout];
                data.Shapes[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthPout];
                data.Shapes[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthPout];
                data.Shapes[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthPout];

                data.Shapes[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];
                data.Shapes[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];
                data.Shapes[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];
                data.Shapes[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperOverturn];

                data.Shapes[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperInside];
                data.Shapes[(int)UnifiedExpressions.LipSuckUpperRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperInside];
                data.Shapes[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerInside];
                data.Shapes[(int)UnifiedExpressions.LipSuckLowerRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerInside];

                data.Shapes[(int)UnifiedExpressions.MouthUpperLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperLeft];
                data.Shapes[(int)UnifiedExpressions.MouthUpperRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthUpperRight];
                data.Shapes[(int)UnifiedExpressions.MouthLowerLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerLeft];
                data.Shapes[(int)UnifiedExpressions.MouthLowerRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerRight];

                data.Shapes[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileLeft];
                data.Shapes[(int)UnifiedExpressions.MouthCornerPullRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileRight];
                data.Shapes[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileLeft];
                data.Shapes[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileRight];
                data.Shapes[(int)UnifiedExpressions.MouthFrownLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSadLeft];
                data.Shapes[(int)UnifiedExpressions.MouthFrownRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSadRight];

                data.Shapes[(int)UnifiedExpressions.MouthRaiserUpper].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerOverlay] - external.blend_shape_weight[(int)LipShape_v2.MouthUpperInside];
                data.Shapes[(int)UnifiedExpressions.MouthRaiserLower].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthLowerOverlay];

                #endregion

                #region Direct Cheek

                data.Shapes[(int)UnifiedExpressions.CheekPuffLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.CheekPuffLeft];
                data.Shapes[(int)UnifiedExpressions.CheekPuffRight].Weight = external.blend_shape_weight[(int)LipShape_v2.CheekPuffRight];

                data.Shapes[(int)UnifiedExpressions.CheekSuckLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.CheekSuck];
                data.Shapes[(int)UnifiedExpressions.CheekSuckRight].Weight = external.blend_shape_weight[(int)LipShape_v2.CheekSuck];

                #endregion

                #region Direct Tongue

                data.Shapes[(int)UnifiedExpressions.TongueOut].Weight = (external.blend_shape_weight[(int)LipShape_v2.TongueLongStep1] + external.blend_shape_weight[(int)LipShape_v2.TongueLongStep2]) / 2.0f;
                data.Shapes[(int)UnifiedExpressions.TongueUp].Weight = external.blend_shape_weight[(int)LipShape_v2.TongueUp];
                data.Shapes[(int)UnifiedExpressions.TongueDown].Weight = external.blend_shape_weight[(int)LipShape_v2.TongueDown];
                data.Shapes[(int)UnifiedExpressions.TongueLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.TongueLeft];
                data.Shapes[(int)UnifiedExpressions.TongueRight].Weight = external.blend_shape_weight[(int)LipShape_v2.TongueRight];
                data.Shapes[(int)UnifiedExpressions.TongueRoll].Weight = external.blend_shape_weight[(int)LipShape_v2.TongueRoll];

                #endregion

                // These shapes are not tracked at all by SRanipal, but instead are being treated as enhancements to driving the shapes above.

                #region Emulated Unified Mapping

                data.Shapes[(int)UnifiedExpressions.CheekSquintLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileLeft];
                data.Shapes[(int)UnifiedExpressions.CheekSquintRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileRight];

                data.Shapes[(int)UnifiedExpressions.MouthDimpleLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileLeft];
                data.Shapes[(int)UnifiedExpressions.MouthDimpleRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSmileRight];

                data.Shapes[(int)UnifiedExpressions.MouthStretchLeft].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSadRight];
                data.Shapes[(int)UnifiedExpressions.MouthStretchRight].Weight = external.blend_shape_weight[(int)LipShape_v2.MouthSadRight];

                #endregion
            }
        }
    }
}