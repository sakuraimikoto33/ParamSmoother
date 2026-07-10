using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using okitsu.net.ndparamsmoother.Runtime;
using UnityEditor;
using UnityEngine;

namespace okitsu.net.ndparamsmoother.Editor
{
    [CustomEditor(typeof(OSCmoothSettings))]
    [CanEditMultipleObjects]
    public class OSCmoothSettingsEditor : UnityEditor.Editor
    {
        private static readonly GUIContent LayerContent = new GUIContent(
            "Layer",
            "This selects what VRChat Playable Layer you would like to set up " +
            "the following Binary Animation Layer into. A layer must be populated " +
            "in order for the tool to properly set up an Animation Layer."
        );

        private static readonly GUIContent ConfigContent = new GUIContent(
            "Config",
            "A preset configuration that stores Parameter Configuration data. " +
            "This is intended for saving configurations for use later or sharing."
        );

        private static readonly GUIContent SmoothnessContent = new GUIContent(
            "Smoothness",
            "How much of a percentage the previous float values influence the current one."
        );

        private static readonly GUIContent LocalSmoothnessContent = new GUIContent(
            "Local Smoothness",
            "How much % smoothness you (locally) will see when a parameter " +
            "changes value. Higher values represent more smoothness, and vice versa."
        );

        private static readonly GUIContent RemoteSmoothnessContent = new GUIContent(
            "Remote Smoothness",
            "How much % smoothness remote users will see when a parameter " +
            "changes value. Higher values represent more smoothness, and vice versa."
        );

        private static readonly GUIContent ProxyConversionContent = new GUIContent(
            "Proxy Conversion",
            "Automatically convert existing animations to use the Proxy (output) float."
        );

        private static readonly GUIContent FlipInputOutputContent = new GUIContent(
            "Flip Input/Output",
            "Sets the Base parameter to be the output parameter from the smoother layer, and " +
            "sets the Proxy parameter as the input driver parameter. Useful for apps that can " +
            "drive the Proxy parameter like VRCFaceTracking binary parameters."
        );

        private static readonly GUIContent BinaryResolutionContent = new GUIContent(
            "Binary Resolution",
            "How many steps a Binary Parameter can make. Higher values are more accurate, " +
            "while lower values are more economic for parameter space. Recommended to use a " +
            "Resolution of 16 or less for more space savings."
        );

        private static readonly GUIContent CombinedParameterContent = new GUIContent(
            "Combined Parameter (+1 Bit)",
            "Does this parameter go from positive to negative? " +
            "This option will add an extra bool to keep track of the " +
            "positive/negative of the parameter."
        );

        private static readonly GUIContent AddNewParameterContent = new GUIContent(
            "Add New Parameter",
            "Adds a new Parameter configuration."
        );

        private static readonly string[] BinarySizeOptions =
        {
            "OFF", "2 (1 Bit)", "4 (2 Bit)", "8 (3 Bit)", "16 (4 Bit)", "32 (5 Bit)", "64 (6 Bit)", "128 (7 Bit)"
        };

        private static readonly Dictionary<string, bool> ParameterVisibility = new Dictionary<string, bool>();

        private const string OSCmoothLayerTypeName = "OSCTools.OSCmooth.Types.OSCmoothLayer";

        private ScriptableObject importSource;
        private bool showGlobalConfiguration;
        private bool showParameters = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty targetLayer = serializedObject.FindProperty("targetLayer");
            SerializedProperty configuration = serializedObject.FindProperty("configuration");
            SerializedProperty parameters = serializedObject.FindProperty("parameters");

            EditorGUILayout.PropertyField(targetLayer, LayerContent);

            EditorGUILayout.Space(10f);
            DrawConfigImport();

            EditorGUILayout.Space();
            showGlobalConfiguration = EditorGUILayout.Foldout(showGlobalConfiguration, "Default Parameter Values");
            if (showGlobalConfiguration)
            {
                DrawParameterConfiguration(configuration);
            }

            EditorGUI.indentLevel = 0;
            showParameters = EditorGUILayout.Foldout(showParameters, "Parameter Configuration");
            if (parameters != null && parameters.arraySize > 0)
            {
                if (GUILayout.Button("Remove All"))
                {
                    parameters.ClearArray();
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
            }

            EditorGUI.indentLevel = 0;
            if (showParameters && parameters != null)
            {
                DrawParameters(parameters);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(AddNewParameterContent))
            {
                AddParameter(parameters, configuration);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConfigImport()
        {
            Type layerType = FindType(OSCmoothLayerTypeName);
            if (layerType == null)
            {
                importSource = null;
                return;
            }

            if (importSource != null && !layerType.IsInstanceOfType(importSource))
            {
                importSource = null;
            }

            importSource = EditorGUILayout.ObjectField(
                ConfigContent,
                importSource,
                layerType,
                false
            ) as ScriptableObject;

            using (new EditorGUI.DisabledScope(importSource == null))
            {
                if (GUILayout.Button("Import Config Into Component"))
                {
                    serializedObject.ApplyModifiedProperties();

                    foreach (UnityEngine.Object selectedTarget in targets)
                    {
                        OSCmoothSettings settings = selectedTarget as OSCmoothSettings;
                        if (settings == null)
                        {
                            continue;
                        }

                        Undo.RecordObject(settings, "Import OSCmooth Config");
                        ImportInto(settings, importSource);
                        EditorUtility.SetDirty(settings);
                    }

                    serializedObject.Update();
                }
            }
        }

        private static void DrawParameters(SerializedProperty parameters)
        {
            for (int i = 0; i < parameters.arraySize; i++)
            {
                SerializedProperty parameter = parameters.GetArrayElementAtIndex(i);
                if (parameter == null)
                {
                    continue;
                }

                SerializedProperty paramName = parameter.FindPropertyRelative("paramName");
                string visibilityKey = GetVisibilityKey(parameters, i);
                bool isVisible = ParameterVisibility.TryGetValue(visibilityKey, out bool visible) && visible;

                EditorGUI.indentLevel = 0;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(isVisible ? "v" : ">", GUILayout.Width(20)))
                    {
                        isVisible = !isVisible;
                        ParameterVisibility[visibilityKey] = isVisible;
                    }

                    paramName.stringValue = EditorGUILayout.TextField(paramName.stringValue);

                    Color originalColor = GUI.color;
                    GUI.color = Color.red;
                    if (GUILayout.Button("X", GUILayout.Width(40)))
                    {
                        parameters.DeleteArrayElementAtIndex(i);
                        GUI.color = originalColor;
                        break;
                    }

                    GUI.color = originalColor;
                }

                EditorGUI.indentLevel = 2;
                if (isVisible)
                {
                    DrawParameterConfiguration(parameter);
                }
            }
        }

        private static string GetVisibilityKey(SerializedProperty parameters, int index)
        {
            return parameters.serializedObject.targetObject.GetInstanceID() + ":" + parameters.propertyPath + ":" + index;
        }

        private static void DrawParameterConfiguration(SerializedProperty parameter)
        {
            if (parameter == null)
            {
                return;
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            int previousIndent = EditorGUI.indentLevel;

            EditorGUIUtility.labelWidth = 210;
            EditorGUILayout.LabelField(SmoothnessContent);

            EditorGUI.indentLevel = 3;
            EditorGUIUtility.labelWidth = 220;

            EditorGUILayout.PropertyField(parameter.FindPropertyRelative("localSmoothness"), LocalSmoothnessContent);
            EditorGUILayout.PropertyField(parameter.FindPropertyRelative("remoteSmoothness"), RemoteSmoothnessContent);
            EditorGUILayout.PropertyField(parameter.FindPropertyRelative("convertToProxy"), ProxyConversionContent);
            EditorGUILayout.PropertyField(parameter.FindPropertyRelative("flipInputOutput"), FlipInputOutputContent);

            SerializedProperty binarySizeSelection = parameter.FindPropertyRelative("binarySizeSelection");
            binarySizeSelection.intValue = EditorGUILayout.Popup(
                BinaryResolutionContent,
                Mathf.Clamp(binarySizeSelection.intValue, 0, BinarySizeOptions.Length - 1),
                BinarySizeOptions
            );

            EditorGUILayout.PropertyField(parameter.FindPropertyRelative("combinedParameter"), CombinedParameterContent);

            EditorGUI.indentLevel = previousIndent;
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUILayout.Space();
        }

        private static void AddParameter(SerializedProperty parameters, SerializedProperty configuration)
        {
            int index = parameters.arraySize;
            parameters.InsertArrayElementAtIndex(index);

            SerializedProperty parameter = parameters.GetArrayElementAtIndex(index);
            CopySerializedParameter(configuration, parameter);
        }

        private static void CopySerializedParameter(SerializedProperty source, SerializedProperty destination)
        {
            destination.FindPropertyRelative("paramName").stringValue =
                source.FindPropertyRelative("paramName").stringValue;
            destination.FindPropertyRelative("localSmoothness").floatValue =
                source.FindPropertyRelative("localSmoothness").floatValue;
            destination.FindPropertyRelative("remoteSmoothness").floatValue =
                source.FindPropertyRelative("remoteSmoothness").floatValue;
            destination.FindPropertyRelative("flipInputOutput").boolValue =
                source.FindPropertyRelative("flipInputOutput").boolValue;
            destination.FindPropertyRelative("convertToProxy").boolValue =
                source.FindPropertyRelative("convertToProxy").boolValue;
            destination.FindPropertyRelative("binarySizeSelection").intValue =
                source.FindPropertyRelative("binarySizeSelection").intValue;
            destination.FindPropertyRelative("combinedParameter").boolValue =
                source.FindPropertyRelative("combinedParameter").boolValue;
        }

        private static void ImportInto(OSCmoothSettings settings, ScriptableObject source)
        {
            if (source == null)
            {
                return;
            }

            settings.parameters = CopyParameters(GetMemberValue(source, "parameters"));
            settings.configuration = CopyParameter(GetMemberValue(source, "configuration"));
        }

        private static List<OSCmoothSettingsParameter> CopyParameters(object source)
        {
            List<OSCmoothSettingsParameter> copied = new List<OSCmoothSettingsParameter>();
            if (source is not IEnumerable enumerable)
            {
                return copied;
            }

            foreach (object parameter in enumerable)
            {
                copied.Add(CopyParameter(parameter));
            }

            return copied;
        }

        private static OSCmoothSettingsParameter CopyParameter(object source)
        {
            if (source == null)
            {
                return new OSCmoothSettingsParameter();
            }

            return new OSCmoothSettingsParameter
            {
                localSmoothness = GetFloat(source, "localSmoothness", 0.1f),
                remoteSmoothness = GetFloat(source, "remoteSmoothness", 0.7f),
                paramName = GetString(source, "paramName", "NewParam"),
                flipInputOutput = GetBool(source, "flipInputOutput", false),
                convertToProxy = GetBool(source, "convertToProxy", true),
                binarySizeSelection = GetInt(source, "binarySizeSelection", 0),
                combinedParameter = GetBool(source, "combinedParameter", false)
            };
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static object GetMemberValue(object source, string memberName)
        {
            if (source == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = source.GetType();
            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(source);
            }

            PropertyInfo property = type.GetProperty(memberName, flags);
            return property != null && property.CanRead ? property.GetValue(source) : null;
        }

        private static string GetString(object source, string memberName, string defaultValue)
        {
            return GetMemberValue(source, memberName) as string ?? defaultValue;
        }

        private static float GetFloat(object source, string memberName, float defaultValue)
        {
            object value = GetMemberValue(source, memberName);
            try
            {
                return value == null ? defaultValue : Convert.ToSingle(value);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        private static int GetInt(object source, string memberName, int defaultValue)
        {
            object value = GetMemberValue(source, memberName);
            try
            {
                return value == null ? defaultValue : Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        private static bool GetBool(object source, string memberName, bool defaultValue)
        {
            object value = GetMemberValue(source, memberName);
            try
            {
                return value == null ? defaultValue : Convert.ToBoolean(value);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }
    }
}
