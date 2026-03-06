using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StudyController))]
[CanEditMultipleObjects]
public class StudyControllerEditor : Editor
{
    private SerializedProperty _handScaleFactorLevelsProp;
    private SerializedProperty _detectRadiusLevelsProp;
    private SerializedProperty _currentBlockOrderPositionProp;

    private void OnEnable()
    {
        _handScaleFactorLevelsProp = serializedObject.FindProperty("handScaleFactorLevels");
        _detectRadiusLevelsProp = serializedObject.FindProperty("detectRadiusLevels");
        _currentBlockOrderPositionProp = serializedObject.FindProperty("currentBlockOrderPosition");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (iterator.name == "currentBlockOrderPosition")
            {
                DrawDynamicBlockSelectionPopup();
            }
            else
            {
                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDynamicBlockSelectionPopup()
    {
        int handScaleLevelCount = _handScaleFactorLevelsProp != null ? _handScaleFactorLevelsProp.arraySize : 0;
        int detectRadiusLevelCount = _detectRadiusLevelsProp != null ? _detectRadiusLevelsProp.arraySize : 0;
        int totalBlockCount = handScaleLevelCount * detectRadiusLevelCount;

        if (totalBlockCount <= 0)
        {
            EditorGUILayout.PropertyField(_currentBlockOrderPositionProp);
            EditorGUILayout.HelpBox("当前 Block 总数为 0。请确保 HandScale 与 DetectRadius 都至少有 1 个取值。", MessageType.Warning);
            return;
        }

        string[] options = new string[totalBlockCount];
        for (int i = 0; i < totalBlockCount; i++)
        {
            int oneBasedIndex = i + 1;
            options[i] = $"Block {oneBasedIndex}/{totalBlockCount}";
        }

        int currentValue = Mathf.Clamp(_currentBlockOrderPositionProp.intValue, 1, totalBlockCount);
        int selectedIndex = EditorGUILayout.Popup(
            new GUIContent("Current Block Order Position", "当前要运行第几个 block（按 participantId 对应的 Latin square 顺序解释）。"),
            currentValue - 1,
            options);

        _currentBlockOrderPositionProp.intValue = selectedIndex + 1;
        EditorGUILayout.HelpBox($"当前设置下共有 {totalBlockCount} 个 block。", MessageType.Info);
    }
}
