using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Data;
using Microsoft.CodeAnalysis;
using System.IO;
using System.Xml;
using Unity.VisualScripting;
public class RulesetEditor : EditorWindow
{
    private Vector2 scrollPosition;
    private Dictionary<string, bool> categoryFoldouts = new Dictionary<string, bool>();
    private List<Category> categories = new List<Category>();
    private string selectedOption = "a";
    private string[] options = new string[] { "a", "b", "c", "Create New Ruleset" };
    private Dictionary<string, Dictionary<string, RuleAction>> ruleSetRules = new Dictionary<string, Dictionary<string, RuleAction>>();

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
        InitializeRulesetFiles();
        InitializeRulesetData();
    }

    private void InitializeRulesetFiles()
    {
        options = Directory.GetFiles(Application.dataPath, "*.ruleset", System.IO.SearchOption.AllDirectories);

        selectedOption = options[0];
    }

    private void InitializeRulesetData()
    {
        categories.Clear();

        string targetLabel = "RoslynAnalyzer";

        string[] allAssetGUIDs = AssetDatabase.FindAssets("");

        // 解析选中的RuleSet文件
        if (!string.IsNullOrEmpty(selectedOption))
        {
            RuleSetParser parser = new RuleSetParser();
            parser.ParseRuleSet(selectedOption);
            ruleSetRules = parser.GetRuleSetRules();
        }

        foreach (string assetGUID in allAssetGUIDs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
            if (assetPath.EndsWith(".dll"))
            {
                string[] labels = AssetDatabase.GetLabels(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
                if (System.Array.Exists(labels, label => label == targetLabel))
                {
                    LoadAnalyzer(assetPath);
                }
            }
        }
    }

    void LoadAnalyzer(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        var assemblyName = assembly.GetName().Name;
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
                    var categoryName = diagnostic.Category.Substring(diagnostic.Category.LastIndexOf('.') + 1);
                    categoryName = $"{assemblyName}.{categoryName}";
                    
                    if (!categoryTable.ContainsKey(categoryName))
                    {
                        categoryTable.Add(categoryName, new Category { name = categoryName });
                    }
                    var category = categoryTable[categoryName];
                    RuleEntry ruleEntry = new RuleEntry
                    {
                        enabled = diagnostic.IsEnabledByDefault,
                        id = diagnostic.Id,
                        title = diagnostic.Title.ToString(),
                        severity = diagnostic.DefaultSeverity,
                    };

                    // 检查RuleSet中的规则
                    if (ruleSetRules.TryGetValue(categoryName, out var rules) && rules.TryGetValue(ruleEntry.id, out var action))
                    {
                        ruleEntry.enabled = action.Action != "None";
                        if (action.Action != "None" && action.Action != "Error" && action.Action != "Warning" && action.Action != "Info")
                        {
                            ruleEntry.severity = DiagnosticSeverity.Error; // 根据需要调整默认值
                        }
                        else
                        {
                            ruleEntry.severity = action.Action switch
                            {
                                "Error" => DiagnosticSeverity.Error,
                                "Warning" => DiagnosticSeverity.Warning,
                                "Info" => DiagnosticSeverity.Info,
                                _ => DiagnosticSeverity.Error
                            };
                        }
                    }

                    category.entries.Add(ruleEntry);
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
        var preSelected = selectedOption;
        index = EditorGUILayout.Popup(index, options.Select(option => Path.GetFileNameWithoutExtension(option)).ToArray(), GUILayout.Width(150));
        selectedOption = options[index];

        if (selectedOption == "Create New Ruleset")
        {
            CreateNewRuleset();
        }
        else if (preSelected != selectedOption)
        {
            InitializeRulesetFiles();
            InitializeRulesetData();
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
            "Default",
            "ruleset"
        );

        if (!string.IsNullOrEmpty(path))
        {
            // 创建并保存新的规则集文件
            // 这里可以添加创建规则集文件的逻辑
            Debug.Log("创建了新的规则集文件：" + path);
        }
        selectedOption = options[0];
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


public class RuleSetParser
{
    private Dictionary<string, Dictionary<string, RuleAction>> ruleSetRules = new Dictionary<string, Dictionary<string, RuleAction>>();

    public void ParseRuleSet(string ruleSetPath)
    {
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(ruleSetPath);

        XmlElement root = xmlDoc.DocumentElement;
        foreach (XmlElement rulesElement in root.GetElementsByTagName("Rules"))
        {
            string analyzerId = rulesElement.GetAttribute("AnalyzerId");
            string ruleNamespace = rulesElement.GetAttribute("RuleNamespace");

            foreach (XmlElement ruleElement in rulesElement.GetElementsByTagName("Rule"))
            {
                string ruleId = ruleElement.GetAttribute("Id");
                string action = ruleElement.GetAttribute("Action");

                if (!ruleSetRules.ContainsKey(ruleNamespace))
                {
                    ruleSetRules[ruleNamespace] = new Dictionary<string, RuleAction>();
                }
                ruleSetRules[ruleNamespace][ruleId] = new RuleAction { Action = action };
            }
        }
    }

    public Dictionary<string, Dictionary<string, RuleAction>> GetRuleSetRules()
    {
        return ruleSetRules;
    }
}

public class RuleAction
{
    public string Action { get; set; }
}