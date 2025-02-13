using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Data;
using Microsoft.CodeAnalysis;
public class RulesetEditor : EditorWindow
{
    private Vector2 scrollPosition;
    private Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
    private List<Category> categories = new List<Category>();
    private string selectedOption = "a";
    private string[] options = new string[] { "a", "b", "c", "Create New Ruleset" };

    // 定义数据结构
    [System.Serializable]
    public class RuleEntry
    {
        public bool enabled;
        public string id;
        public string title;
        public DiagnosticSeverity severity;
    }

    [System.Serializable]
    public class Category
    {
        public string name;
        public List<RuleEntry> entries = new List<RuleEntry>();
    }

    [MenuItem("Window/Rulesets")]
    public static void ShowWindow()
    {
        GetWindow<RulesetEditor>("Rulesets");
    }

    private void OnEnable()
    {
        InitializeSampleData();
    }

    private void InitializeSampleData()
    {
        categories.Clear();

        string targetLabel = "RoslynAnalyzer";

        string[] allAssetGUIDs = AssetDatabase.FindAssets("");

        foreach (string assetGUID in allAssetGUIDs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
            if (assetPath.EndsWith(".dll"))
            {
                string[] labels = AssetDatabase.GetLabels(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
                if (System.Array.Exists(labels, label => label == targetLabel))
                {
                    if (!assetPath.Contains("CodeFixes"))
                    {
                        LoadAnalyzer(assetPath);
                    }
                }
            }
        }
    }

    void LoadAnalyzer(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        var diagnosticAnalyzerTypes = assembly.GetTypes()
                                            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract)
                                            .ToList();
        Dictionary<string, Category> categoryTable = new Dictionary<string, Category>();

        foreach (var type in diagnosticAnalyzerTypes)
        {
            var analyzerInstance = Activator.CreateInstance(type) as DiagnosticAnalyzer;
            if (analyzerInstance != null)
            {
                System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.DiagnosticDescriptor> supportedDiagnostics = analyzerInstance.SupportedDiagnostics;
                foreach (var diagnostic in supportedDiagnostics)
                {
                    if (!categoryTable.ContainsKey(diagnostic.Category))
                    {
                        categoryTable.Add(diagnostic.Category, new Category { name = diagnostic.Category });
                    }
                    var category = categoryTable[diagnostic.Category];
                    category.entries.Add(new RuleEntry
                    {
                        enabled = diagnostic.IsEnabledByDefault,
                        id = diagnostic.Id,
                        title = diagnostic.Title.ToString(),
                        severity = diagnostic.DefaultSeverity,
                    });
                }
            }
        }

        categories.AddRange(categoryTable.Values);
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawContent();
    }

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        var index = Array.IndexOf(options, selectedOption);

        index = EditorGUILayout.Popup(index, options, GUILayout.Width(150));
        selectedOption = options[index];

        if (selectedOption == "Create New Ruleset")
        {
            CreateNewRuleset();
        }

        // 绘制分隔线
        GUILayout.FlexibleSpace();
        GUILayout.Label("Search:");

        // 搜索栏
        searchString = GUILayout.TextField(searchString, GUILayout.Width(150));

        GUILayout.EndHorizontal();
    }

    private string searchString = "";

    private void CreateNewRuleset()
    {
        string path = EditorUtility.SaveFilePanel(
            "Create New Ruleset",
            "",
            "NewRuleset",
            "ruleset"
        );

        if (!string.IsNullOrEmpty(path))
        {
            // 创建并保存新的规则集文件
            // 这里可以添加创建规则集文件的逻辑
            Debug.Log("创建了新的规则集文件：" + path);
        }
        selectedOption = "a";
    }

    private void DrawContent()
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        foreach (var category in categories)
        {
            bool hasMatchingRules = category.entries.Any(e =>
                e.id.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                e.title.Contains(searchString, StringComparison.OrdinalIgnoreCase));

            if (hasMatchingRules)
            {
                if (!categoryFoldouts.ContainsKey(category.name))
                {
                    categoryFoldouts[category.name] = false;
                }

                if (searchString != "")
                {
                    categoryFoldouts[category.name] = true;
                }

                bool isExpanded = categoryFoldouts[category.name];
                bool allEnabled = category.entries.Count == 0 ? false : category.entries.All(e => e.enabled);
                bool someEnabled = !allEnabled && category.entries.Any(e => e.enabled);

                EditorGUILayout.BeginHorizontal(EditorStyles.foldoutHeader);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = someEnabled;
                Rect toggleRect = EditorGUILayout.GetControlRect(GUILayout.Width(20));
                bool newToggle = EditorGUI.Toggle(toggleRect, allEnabled);
                EditorGUI.showMixedValue = false;

                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var entry in category.entries)
                    {
                        entry.enabled = newToggle;
                    }
                }

                GUILayout.Label(category.name, GUILayout.ExpandWidth(true));

                GUIContent arrowIcon = isExpanded
                    ? EditorGUIUtility.IconContent("IN foldout on")
                    : EditorGUIUtility.IconContent("IN foldout");
                GUILayout.Label(arrowIcon, GUILayout.Width(20));

                EditorGUILayout.EndHorizontal();

                Rect headerRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
                {
                    if (!toggleRect.Contains(Event.current.mousePosition))
                    {
                        categoryFoldouts[category.name] = !isExpanded;
                        Event.current.Use();
                    }
                }

                if (categoryFoldouts[category.name])
                {
                    EditorGUI.indentLevel++;
                    foreach (var entry in category.entries)
                    {
                        if (entry.id.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                            entry.title.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                        {
                            GUILayout.BeginHorizontal();
                            entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(20));
                            GUILayout.Space(10);
                            GUILayout.Label(entry.id, GUILayout.Width(60), GUILayout.ExpandWidth(false));
                            GUILayout.Label(entry.title, GUILayout.ExpandWidth(true));
                            entry.severity = (DiagnosticSeverity)EditorGUILayout.EnumPopup(
                                entry.severity,
                                GUILayout.Width(80),
                                GUILayout.ExpandWidth(false)
                            );
                            GUILayout.EndHorizontal();
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        GUILayout.EndScrollView();
    }
}