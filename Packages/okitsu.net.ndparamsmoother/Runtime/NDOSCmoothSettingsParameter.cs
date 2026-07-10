using System;

namespace okitsu.net.ndparamsmoother.Runtime
{
    [Serializable]
    public class OSCmoothSettingsParameter
    {
        public float localSmoothness = 0.1f;
        public float remoteSmoothness = 0.7f;
        public string paramName = "NewParam";
        public bool flipInputOutput = false;
        public bool convertToProxy = true;
        public int binarySizeSelection = 0;
        public bool combinedParameter = false;
        [NonSerialized]
        public bool isVisible;

        public OSCmoothSettingsParameter() { }

        public OSCmoothSettingsParameter(OSCmoothSettingsParameter source)
        {
            if (source == null)
            {
                return;
            }

            localSmoothness = source.localSmoothness;
            remoteSmoothness = source.remoteSmoothness;
            paramName = source.paramName;
            flipInputOutput = source.flipInputOutput;
            convertToProxy = source.convertToProxy;
            binarySizeSelection = source.binarySizeSelection;
            combinedParameter = source.combinedParameter;
        }
    }
}
