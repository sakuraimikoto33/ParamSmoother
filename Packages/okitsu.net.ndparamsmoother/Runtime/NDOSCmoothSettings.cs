using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace okitsu.net.ndparamsmoother.Runtime
{
    [AddComponentMenu("Oktnet/Parameter Smoother")]
    public class OSCmoothSettings : MonoBehaviour, IEditorOnly
    {
        public VRCAvatarDescriptor.AnimLayerType targetLayer = VRCAvatarDescriptor.AnimLayerType.FX;
        public List<OSCmoothSettingsParameter> parameters = new List<OSCmoothSettingsParameter>();
        public OSCmoothSettingsParameter configuration = new OSCmoothSettingsParameter();

        public static List<OSCmoothSettingsParameter> CopyParameters(IEnumerable<OSCmoothSettingsParameter> source)
        {
            List<OSCmoothSettingsParameter> copied = new List<OSCmoothSettingsParameter>();
            if (source == null)
            {
                return copied;
            }

            foreach (OSCmoothSettingsParameter parameter in source)
            {
                copied.Add(CopyParameter(parameter));
            }

            return copied;
        }

        public static OSCmoothSettingsParameter CopyParameter(OSCmoothSettingsParameter source)
        {
            return source == null
                ? new OSCmoothSettingsParameter()
                : new OSCmoothSettingsParameter(source);
        }

        private void Reset()
        {
            EnsureDefaults();
        }

        private void OnValidate()
        {
            EnsureDefaults();
        }

        private void EnsureDefaults()
        {
            if (parameters == null)
            {
                parameters = new List<OSCmoothSettingsParameter>();
            }

            if (configuration == null)
            {
                configuration = new OSCmoothSettingsParameter();
            }
        }
    }
}
