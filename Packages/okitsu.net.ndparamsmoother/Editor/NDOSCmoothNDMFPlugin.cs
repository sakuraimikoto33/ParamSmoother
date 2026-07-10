using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEngine;

[assembly: ExportsPlugin(typeof(okitsu.net.ndparamsmoother.Editor.OSCmoothNDMFPlugin))]

namespace okitsu.net.ndparamsmoother.Editor
{
    public class OSCmoothNDMFPlugin : Plugin<OSCmoothNDMFPlugin>
    {
        public override string QualifiedName => "okitsu.net.paramsmoother";
        public override string DisplayName => "Param Smoother";
        public override Color? ThemeColor => new Color(0.25f, 0.7f, 0.95f);

        protected override void Configure()
        {
            Sequence sequence = InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar");

            sequence.WithRequiredExtension(typeof(AnimatorServicesContext), scoped =>
            {
                scoped.Run("Clone animators for OSCmooth", _ => { });
            });

            sequence.Run("Apply OSCmooth", OSCmoothNonDestructiveProcessor.Execute);
        }
    }
}
