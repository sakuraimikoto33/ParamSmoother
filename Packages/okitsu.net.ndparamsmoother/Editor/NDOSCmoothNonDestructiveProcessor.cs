using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using okitsu.net.ndparamsmoother.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace okitsu.net.ndparamsmoother.Editor
{
    internal static class OSCmoothNonDestructiveProcessor
    {
        private const string LogPrefix = "[ParamSmoother]";
        private const string BlendSetParameter = "OSCm/BlendSet";
        private const string SmoothingLayerName = "_OSCmooth_Smoothing_Gen";
        private const string BinaryLayerName = "_OSCmooth_Binary_Gen";
        private const string LocalSmootherPrefix = "OSCm/Local/";
        private const string RemoteSmootherPrefix = "OSCm/Remote/";
        private const string ProxyPrefix = "OSCm/Proxy/";

        private static readonly string[] ParameterExtensions =
        {
            "OSCm/Proxy/",
            "OSCm_Proxy"
        };

        public static void Execute(BuildContext context)
        {
            OSCmoothSettings[] allSettings = context.AvatarRootObject.GetComponentsInChildren<OSCmoothSettings>(true);
            if (allSettings.Length == 0)
            {
                return;
            }

            VRCAvatarDescriptor descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogWarning($"{LogPrefix} Avatar descriptor not found, skipping OSCmooth.");
                DestroySettings(allSettings);
                return;
            }

            // The previous pass activates animator services to clone controllers. Make sure those clones are committed
            // before this legacy-style AnimatorController manipulation runs.
            context.DeactivateExtensionContext<AnimatorServicesContext>();
            context.DeactivateExtensionContext<VirtualControllerContext>();

            Dictionary<VRCAvatarDescriptor.AnimLayerType, List<OSCmoothSettingsParameter>> groupedParameters =
                CollectParameters(allSettings);

            foreach (KeyValuePair<VRCAvatarDescriptor.AnimLayerType, List<OSCmoothSettingsParameter>> group in groupedParameters)
            {
                if (group.Value.Count == 0)
                {
                    continue;
                }

                AnimatorController controller = FindAnimatorController(descriptor, group.Key);
                if (controller == null)
                {
                    Debug.LogWarning($"{LogPrefix} {group.Key} layer does not have an AnimatorController, skipping.");
                    continue;
                }

                if (!context.IsTemporaryAsset(controller))
                {
                    Debug.LogError($"{LogPrefix} {group.Key} controller is not an NDMF temporary clone; skipping to avoid destructive edits: {controller.name}");
                    continue;
                }

                Processor processor = new Processor(context, controller, group.Value);
                processor.Apply();
                Debug.Log($"{LogPrefix} Applied OSCmooth to temporary {group.Key} controller: {controller.name}");
            }

            DestroySettings(allSettings);
        }

        private static Dictionary<VRCAvatarDescriptor.AnimLayerType, List<OSCmoothSettingsParameter>> CollectParameters(
            IEnumerable<OSCmoothSettings> settingsComponents
        )
        {
            Dictionary<VRCAvatarDescriptor.AnimLayerType, LayerAccumulator> accumulators =
                new Dictionary<VRCAvatarDescriptor.AnimLayerType, LayerAccumulator>();

            foreach (OSCmoothSettings settings in settingsComponents)
            {
                if (settings == null || !settings.enabled)
                {
                    continue;
                }

                if (!accumulators.TryGetValue(settings.targetLayer, out LayerAccumulator accumulator))
                {
                    accumulator = new LayerAccumulator(settings.targetLayer);
                    accumulators.Add(settings.targetLayer, accumulator);
                }

                IEnumerable<OSCmoothSettingsParameter> parameters =
                    settings.parameters ?? Enumerable.Empty<OSCmoothSettingsParameter>();
                foreach (OSCmoothSettingsParameter parameter in parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.paramName))
                    {
                        continue;
                    }

                    accumulator.Add(OSCmoothSettings.CopyParameter(parameter), settings);
                }
            }

            return accumulators.ToDictionary(pair => pair.Key, pair => pair.Value.Parameters);
        }

        private static AnimatorController FindAnimatorController(
            VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType targetLayer
        )
        {
            AnimatorController controller = FindAnimatorController(descriptor.baseAnimationLayers, targetLayer);
            return controller != null
                ? controller
                : FindAnimatorController(descriptor.specialAnimationLayers, targetLayer);
        }

        private static AnimatorController FindAnimatorController(
            VRCAvatarDescriptor.CustomAnimLayer[] layers,
            VRCAvatarDescriptor.AnimLayerType targetLayer
        )
        {
            if (layers == null)
            {
                return null;
            }

            foreach (VRCAvatarDescriptor.CustomAnimLayer layer in layers)
            {
                if (layer.type == targetLayer)
                {
                    return layer.animatorController as AnimatorController;
                }
            }

            return null;
        }

        private static void DestroySettings(IEnumerable<OSCmoothSettings> settingsComponents)
        {
            foreach (OSCmoothSettings settings in settingsComponents)
            {
                if (settings != null)
                {
                    Object.DestroyImmediate(settings);
                }
            }
        }

        private sealed class LayerAccumulator
        {
            private readonly VRCAvatarDescriptor.AnimLayerType targetLayer;
            private readonly Dictionary<string, int> indicesByName = new Dictionary<string, int>();

            public List<OSCmoothSettingsParameter> Parameters { get; } = new List<OSCmoothSettingsParameter>();

            public LayerAccumulator(VRCAvatarDescriptor.AnimLayerType targetLayer)
            {
                this.targetLayer = targetLayer;
            }

            public void Add(OSCmoothSettingsParameter parameter, OSCmoothSettings source)
            {
                if (indicesByName.TryGetValue(parameter.paramName, out int index))
                {
                    Debug.LogWarning(
                        $"{LogPrefix} Duplicate parameter '{parameter.paramName}' on {targetLayer}; using later OSCmoothSettings from {source.name}."
                    );
                    Parameters[index] = parameter;
                    return;
                }

                indicesByName.Add(parameter.paramName, Parameters.Count);
                Parameters.Add(parameter);
            }
        }

        private sealed class Processor
        {
            private readonly BuildContext context;
            private readonly AnimatorController controller;
            private readonly List<OSCmoothSettingsParameter> parameters;

            public Processor(BuildContext context, AnimatorController controller, List<OSCmoothSettingsParameter> parameters)
            {
                this.context = context;
                this.controller = controller;
                this.parameters = parameters;
            }

            public void Apply()
            {
                RevertStateMachineParameters();
                RemoveGeneratedParameters();
                RemoveGeneratedLayers();

                if (parameters.Any(parameter => parameter.binarySizeSelection > 0))
                {
                    CreateBinaryLayer();
                }

                CreateSmoothAnimationLayer();
                EditorUtility.SetDirty(controller);
            }

            private void CreateSmoothAnimationLayer()
            {
                AnimatorControllerLayer layer = CreateLayer(SmoothingLayerName);

                AnimatorState localState = AddState(layer.stateMachine, "OSCmooth_Local", new Vector3(30, 170, 0));
                AnimatorState remoteState = AddState(layer.stateMachine, "OSCmooth_Net", new Vector3(30, 230, 0));
                localState.writeDefaultValues = true;
                remoteState.writeDefaultValues = true;

                AnimatorStateTransition toRemoteState = localState.AddTransition(remoteState);
                ConfigureInstantTransition(toRemoteState);
                Save(toRemoteState);

                AnimatorStateTransition toLocalState = remoteState.AddTransition(localState);
                ConfigureInstantTransition(toLocalState);
                Save(toLocalState);

                if (IsLocalParameterIsFloat())
                {
                    toRemoteState.AddCondition(AnimatorConditionMode.Less, 0.5f, "IsLocal");
                    toLocalState.AddCondition(AnimatorConditionMode.Greater, 0.5f, "IsLocal");
                }
                else
                {
                    CheckAndCreateParameter("IsLocal", AnimatorControllerParameterType.Bool);
                    toRemoteState.AddCondition(AnimatorConditionMode.IfNot, 0, "IsLocal");
                    toLocalState.AddCondition(AnimatorConditionMode.If, 0, "IsLocal");
                }

                BlendTree localRoot = CreateDirectBlendTree("OSCm_Local");
                BlendTree remoteRoot = CreateDirectBlendTree("OSCm_Remote");

                localState.motion = localRoot;
                remoteState.motion = remoteRoot;

                CheckAndCreateParameter(BlendSetParameter, AnimatorControllerParameterType.Float, 1f);

                List<ChildMotion> localChildren = new List<ChildMotion>();
                List<ChildMotion> remoteChildren = new List<ChildMotion>();

                foreach (OSCmoothSettingsParameter parameter in parameters)
                {
                    if (parameter.convertToProxy)
                    {
                        RenameAllStateMachineInstancesOfBlendParameter(parameter.paramName, ProxyPrefix + parameter.paramName);
                    }

                    BlendTree localMotion = CreateSmoothingBlendTree(
                        parameter.localSmoothness,
                        parameter.paramName,
                        parameter.flipInputOutput,
                        1f,
                        LocalSmootherPrefix,
                        "SmootherWD"
                    );

                    BlendTree remoteMotion = CreateSmoothingBlendTree(
                        parameter.remoteSmoothness,
                        parameter.paramName,
                        parameter.flipInputOutput,
                        1f,
                        RemoteSmootherPrefix,
                        "SmootherRemoteWD"
                    );

                    localChildren.Add(CreateDirectChild(localMotion));
                    remoteChildren.Add(CreateDirectChild(remoteMotion));
                }

                localRoot.children = localChildren.ToArray();
                remoteRoot.children = remoteChildren.ToArray();
                MarkDirty(localRoot, remoteRoot, localState, remoteState, layer.stateMachine);
            }

            private void CreateBinaryLayer()
            {
                AnimatorControllerLayer layer = CreateLayer(BinaryLayerName);

                AnimatorState state = AddState(layer.stateMachine, "Binary_Parameters_Blendtree", new Vector3(30, 170, 0));
                state.writeDefaultValues = true;

                BlendTree binaryRoot = CreateDirectBlendTree("Binary_Root");
                state.motion = binaryRoot;

                CheckAndCreateParameter(BlendSetParameter, AnimatorControllerParameterType.Float, 1f);

                List<ChildMotion> children = new List<ChildMotion>();
                foreach (OSCmoothSettingsParameter parameter in parameters)
                {
                    if (parameter.binarySizeSelection <= 0)
                    {
                        continue;
                    }

                    BlendTree decodeBinary = CreateBinaryBlendTree(
                        layer.stateMachine,
                        parameter.paramName,
                        parameter.binarySizeSelection,
                        parameter.combinedParameter
                    );

                    children.Add(CreateDirectChild(decodeBinary));
                }

                binaryRoot.children = children.ToArray();
                MarkDirty(binaryRoot, state, layer.stateMachine);
            }

            private BlendTree CreateSmoothingBlendTree(
                float smoothness,
                string paramName,
                bool driveBase,
                float range,
                string smoothnessPrefix,
                string smoothnessSuffix
            )
            {
                CheckAndCreateParameter(smoothnessPrefix + paramName + "Smoother", AnimatorControllerParameterType.Float, smoothness);
                CheckAndCreateParameter(ProxyPrefix + paramName, AnimatorControllerParameterType.Float);
                CheckAndCreateParameter(paramName, AnimatorControllerParameterType.Float);

                BlendTree rootTree = new BlendTree
                {
                    blendType = BlendTreeType.Simple1D,
                    hideFlags = HideFlags.HideInHierarchy,
                    blendParameter = smoothnessPrefix + paramName + "Smoother",
                    name = "OSCm_" + paramName + " Root",
                    useAutomaticThresholds = false
                };
                Save(rootTree);

                BlendTree falseTree = new BlendTree
                {
                    blendType = BlendTreeType.Simple1D,
                    hideFlags = HideFlags.HideInHierarchy,
                    blendParameter = driveBase ? ProxyPrefix + paramName : paramName,
                    name = "OSCm_Input",
                    useAutomaticThresholds = false
                };
                Save(falseTree);

                BlendTree trueTree = new BlendTree
                {
                    blendType = BlendTreeType.Simple1D,
                    hideFlags = HideFlags.HideInHierarchy,
                    blendParameter = driveBase ? paramName : ProxyPrefix + paramName,
                    name = "OSCm_Driver",
                    useAutomaticThresholds = false
                };
                Save(trueTree);

                AnimationClip[] driverClips = CreateFloatSmootherAnimation(
                    paramName,
                    smoothnessSuffix,
                    -range,
                    range,
                    driveBase
                );

                rootTree.AddChild(falseTree, driveBase ? 1f : 0f);
                rootTree.AddChild(trueTree, driveBase ? 0f : 1f);

                falseTree.AddChild(driverClips[0], -1f);
                falseTree.AddChild(driverClips[1], 1f);

                trueTree.AddChild(driverClips[0], -1f);
                trueTree.AddChild(driverClips[1], 1f);

                MarkDirty(rootTree, falseTree, trueTree);
                return rootTree;
            }

            private BlendTree CreateBinaryBlendTree(
                AnimatorStateMachine stateMachine,
                string paramName,
                int binarySizeSelection,
                bool combinedParameter
            )
            {
                string blendRootParameter = BlendSetParameter;
                if (combinedParameter)
                {
                    CheckAndCreateParameter(paramName + "Negative", AnimatorControllerParameterType.Float);
                    blendRootParameter = paramName + "Negative";
                }

                BlendTree decodeBinaryRoot = new BlendTree
                {
                    blendType = BlendTreeType.Simple1D,
                    hideFlags = HideFlags.HideInHierarchy,
                    blendParameter = blendRootParameter,
                    name = "Binary_" + paramName + "_Root",
                    useAutomaticThresholds = false
                };
                Save(decodeBinaryRoot);

                BlendTree decodeBinaryPositiveTree = CreateDirectBlendTree("Binary_" + paramName + "_Positive");
                BlendTree decodeBinaryNegativeTree = CreateDirectBlendTree("Binary_" + paramName + "_Negative");

                List<ChildMotion> positiveChildren = new List<ChildMotion>();
                List<ChildMotion> negativeChildren = new List<ChildMotion>();

                for (int i = 0; i < binarySizeSelection; i++)
                {
                    BlendTree positiveDecode = CreateBinaryDecode(stateMachine, paramName, i, binarySizeSelection, false);
                    positiveChildren.Add(CreateDirectChild(positiveDecode));
                }

                if (combinedParameter)
                {
                    for (int i = 0; i < binarySizeSelection; i++)
                    {
                        BlendTree negativeDecode = CreateBinaryDecode(stateMachine, paramName, i, binarySizeSelection, true);
                        negativeChildren.Add(CreateDirectChild(negativeDecode));
                    }
                }

                decodeBinaryPositiveTree.children = positiveChildren.ToArray();
                decodeBinaryNegativeTree.children = negativeChildren.ToArray();

                decodeBinaryRoot.AddChild(decodeBinaryPositiveTree, 0f);
                if (combinedParameter)
                {
                    decodeBinaryRoot.AddChild(decodeBinaryNegativeTree, 1f);
                }

                MarkDirty(decodeBinaryRoot, decodeBinaryPositiveTree, decodeBinaryNegativeTree);
                return decodeBinaryRoot;
            }

            private BlendTree CreateBinaryDecode(
                AnimatorStateMachine stateMachine,
                string paramName,
                int binaryPow,
                int binarySize,
                bool negative
            )
            {
                int bitWeight = 1 << binaryPow;
                CheckAndCreateParameter(paramName + bitWeight, AnimatorControllerParameterType.Float);

                BlendTree decodeBinary = new BlendTree
                {
                    blendType = BlendTreeType.Simple1D,
                    hideFlags = HideFlags.HideInHierarchy,
                    blendParameter = paramName + bitWeight,
                    name = "Binary_" + paramName + "_Decode_" + bitWeight,
                    useAutomaticThresholds = false
                };
                Save(decodeBinary);

                float weight = (negative ? -0.5f : 0.5f) * Mathf.Pow(2, binaryPow + 1) / (Mathf.Pow(2, binarySize) - 1f);
                AnimationClip[] clips = CreateBinaryAnimation(paramName, weight, binaryPow);
                decodeBinary.AddChild(clips[0], 0f);
                decodeBinary.AddChild(clips[1], 1f);

                MarkDirty(decodeBinary, stateMachine);
                return decodeBinary;
            }

            private AnimationClip[] CreateFloatSmootherAnimation(
                string paramName,
                string smoothSuffix,
                float initThreshold,
                float finalThreshold,
                bool driveBase
            )
            {
                string driveParameter = driveBase ? paramName : ProxyPrefix + paramName;
                AnimationClip initialClip = CreateClip(NameNoSymbol(paramName) + "-1" + smoothSuffix);
                initialClip.SetCurve("", typeof(Animator), driveParameter, new AnimationCurve(new Keyframe(0.0f, initThreshold)));

                AnimationClip finalClip = CreateClip(NameNoSymbol(paramName) + "1" + smoothSuffix);
                finalClip.SetCurve("", typeof(Animator), driveParameter, new AnimationCurve(new Keyframe(0.0f, finalThreshold)));

                MarkDirty(initialClip, finalClip);
                return new[] { initialClip, finalClip };
            }

            private AnimationClip[] CreateBinaryAnimation(string paramName, float weight, int step)
            {
                string suffix = step + "_" + (weight < 0 ? "Negative" : "Positive");
                AnimationClip falseClip = CreateClip(NameNoSymbol(paramName) + "_False_" + suffix);
                falseClip.SetCurve("", typeof(Animator), paramName, new AnimationCurve(new Keyframe(0.0f, 0.0f)));

                AnimationClip trueClip = CreateClip(NameNoSymbol(paramName) + "_True_" + suffix);
                trueClip.SetCurve("", typeof(Animator), paramName, new AnimationCurve(new Keyframe(0.0f, weight)));

                MarkDirty(falseClip, trueClip);
                return new[] { falseClip, trueClip };
            }

            private AnimationClip CreateClip(string name)
            {
                AnimationClip clip = new AnimationClip
                {
                    name = name,
                    hideFlags = HideFlags.HideInHierarchy
                };
                Save(clip);
                return clip;
            }

            private AnimatorControllerLayer CreateLayer(string layerName)
            {
                for (int i = 0; i < controller.layers.Length; i++)
                {
                    if (controller.layers[i].name == layerName)
                    {
                        controller.RemoveLayer(i);
                        break;
                    }
                }

                AnimatorStateMachine stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                };
                Save(stateMachine);

                AnimatorControllerLayer layer = new AnimatorControllerLayer
                {
                    name = layerName,
                    stateMachine = stateMachine,
                    defaultWeight = 1f
                };

                controller.AddLayer(layer);
                return layer;
            }

            private AnimatorState AddState(AnimatorStateMachine stateMachine, string stateName, Vector3 position)
            {
                AnimatorState state = stateMachine.AddState(stateName, position);
                state.hideFlags = HideFlags.HideInHierarchy;
                Save(state);
                return state;
            }

            private BlendTree CreateDirectBlendTree(string name)
            {
                BlendTree blendTree = new BlendTree
                {
                    blendType = BlendTreeType.Direct,
                    hideFlags = HideFlags.HideInHierarchy,
                    name = name,
                    useAutomaticThresholds = false
                };
                Save(blendTree);
                return blendTree;
            }

            private static ChildMotion CreateDirectChild(Motion motion)
            {
                return new ChildMotion
                {
                    directBlendParameter = BlendSetParameter,
                    motion = motion,
                    timeScale = 1
                };
            }

            private void CheckAndCreateParameter(
                string paramName,
                AnimatorControllerParameterType type,
                double defaultValue = 0
            )
            {
                AnimatorControllerParameter parameter = new AnimatorControllerParameter
                {
                    name = paramName,
                    type = type,
                    defaultFloat = (float)defaultValue,
                    defaultInt = (int)defaultValue,
                    defaultBool = Math.Abs(defaultValue) > double.Epsilon
                };

                List<AnimatorControllerParameter> controllerParameters = controller.parameters.ToList();
                int existingIndex = controllerParameters.FindIndex(existing => existing.name == paramName);
                if (existingIndex >= 0)
                {
                    controllerParameters[existingIndex] = parameter;
                }
                else
                {
                    controllerParameters.Add(parameter);
                }

                controller.parameters = controllerParameters.ToArray();
            }

            private bool IsLocalParameterIsFloat()
            {
                return controller.parameters.Any(parameter =>
                    parameter.name == "IsLocal" && parameter.type == AnimatorControllerParameterType.Float
                );
            }

            private void RemoveGeneratedParameters()
            {
                controller.parameters = controller.parameters
                    .Where(parameter => !parameter.name.Contains("OSCm"))
                    .ToArray();
            }

            private void RemoveGeneratedLayers()
            {
                for (int i = 0; i < controller.layers.Length;)
                {
                    if (controller.layers[i].name.Contains("OSCm"))
                    {
                        controller.RemoveLayer(i);
                        continue;
                    }

                    i++;
                }
            }

            private void RevertStateMachineParameters()
            {
                foreach (AnimatorControllerLayer layer in controller.layers)
                {
                    RenameInStateMachine(layer.stateMachine, ReplaceOSCmoothParameterExtension);
                }
            }

            private string ReplaceOSCmoothParameterExtension(string parameterName)
            {
                if (string.IsNullOrEmpty(parameterName))
                {
                    return parameterName;
                }

                foreach (string extension in ParameterExtensions)
                {
                    if (parameterName.Contains(extension))
                    {
                        return parameterName.Replace(extension, "");
                    }
                }

                return parameterName;
            }

            private void RenameAllStateMachineInstancesOfBlendParameter(string initialParameter, string newParameter)
            {
                foreach (AnimatorControllerLayer layer in controller.layers)
                {
                    RenameInStateMachine(layer.stateMachine, parameterName =>
                        parameterName == initialParameter ? newParameter : parameterName
                    );
                }
            }

            private void RenameInStateMachine(AnimatorStateMachine stateMachine, Func<string, string> rename)
            {
                if (stateMachine == null)
                {
                    return;
                }

                foreach (ChildAnimatorState childState in stateMachine.states)
                {
                    RenameInState(childState.state, rename);
                }

                foreach (ChildAnimatorStateMachine childStateMachine in stateMachine.stateMachines)
                {
                    RenameInStateMachine(childStateMachine.stateMachine, rename);
                }
            }

            private void RenameInState(AnimatorState state, Func<string, string> rename)
            {
                if (state == null)
                {
                    return;
                }

                state.timeParameter = rename(state.timeParameter);
                state.speedParameter = rename(state.speedParameter);
                state.cycleOffsetParameter = rename(state.cycleOffsetParameter);
                state.mirrorParameter = rename(state.mirrorParameter);

                if (state.motion is BlendTree blendTree)
                {
                    RenameInBlendTree(blendTree, rename);
                }

                EditorUtility.SetDirty(state);
            }

            private void RenameInBlendTree(BlendTree blendTree, Func<string, string> rename)
            {
                if (blendTree == null)
                {
                    return;
                }

                blendTree.blendParameter = rename(blendTree.blendParameter);
                blendTree.blendParameterY = rename(blendTree.blendParameterY);

                ChildMotion[] children = blendTree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    children[i].directBlendParameter = rename(children[i].directBlendParameter);
                    if (children[i].motion is BlendTree childBlendTree)
                    {
                        RenameInBlendTree(childBlendTree, rename);
                    }
                }

                blendTree.children = children;
                EditorUtility.SetDirty(blendTree);
            }

            private static void ConfigureInstantTransition(AnimatorStateTransition transition)
            {
                transition.canTransitionToSelf = false;
                transition.hasExitTime = false;
                transition.duration = 0;
                transition.offset = 0;
                transition.exitTime = 0;
                transition.hasFixedDuration = true;
            }

            private void Save(Object asset)
            {
                if (asset == null)
                {
                    return;
                }

                context.AssetSaver.SaveAsset(asset);
                EditorUtility.SetDirty(asset);
            }

            private static void MarkDirty(params Object[] assets)
            {
                foreach (Object asset in assets)
                {
                    if (asset != null)
                    {
                        EditorUtility.SetDirty(asset);
                    }
                }
            }

            private static string NameNoSymbol(string name)
            {
                return string.IsNullOrEmpty(name) ? string.Empty : name.Replace("/", "");
            }
        }
    }
}
