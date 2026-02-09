using UnityEditor;
using UnityEngine;
using Peribind.Unity.Networking;

public class UgsProfileEditorWindow : EditorWindow
{
    private string _profile;

    [MenuItem("Tools/UGS/Profile Override")]
    public static void ShowWindow()
    {
        var window = GetWindow<UgsProfileEditorWindow>(true, "UGS Profile Override");
        window.minSize = new Vector2(360, 110);
        window.Show();
    }

    private void OnEnable()
    {
        _profile = EditorPrefs.GetString(UgsBootstrap.EditorProfilePrefKey, string.Empty);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("UGS Anonymous Profile (Editor Only)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Set a profile name to get a distinct anonymous PlayerId in the Editor. Builds ignore this.", MessageType.Info);
        _profile = EditorGUILayout.TextField("Profile", _profile);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save"))
            {
                EditorPrefs.SetString(UgsBootstrap.EditorProfilePrefKey, _profile ?? string.Empty);
                PlayerPrefs.SetString(UgsBootstrap.EditorProfilePrefKey, _profile ?? string.Empty);
                Debug.Log($"[UGS] Profile override set to '{_profile}'.");
            }

            if (GUILayout.Button("Clear"))
            {
                EditorPrefs.DeleteKey(UgsBootstrap.EditorProfilePrefKey);
                PlayerPrefs.DeleteKey(UgsBootstrap.EditorProfilePrefKey);
                _profile = string.Empty;
                Debug.Log("[UGS] Profile override cleared.");
            }
        }
    }
}
