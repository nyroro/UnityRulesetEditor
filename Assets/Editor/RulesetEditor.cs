﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using UnityEditor;
using UnityEngine;

public class RulesetEditor : EditorWindow
{
    private const string CreateNewRulesetOption = "Create New Ruleset";
    private Vector2 scrollPosition;
    private Dictionary<string, bool> namespaceFoldouts = new Dictionary<string, bool>();
    private List<RuleNamespace> ruleNamespaces = new List<RuleNamespace>();
    private string selectedOption;
    private string[] options;
    private Dictionary<string, Dictionary<string, RuleAction>> ruleSetRules = new Dictionary<string, Dictionary<string, RuleAction>>();

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
        if (options.Length == 0)
        {
            options = new string[] { string.Empty };
        }

        options = options.Append(CreateNewRulesetOption).ToArray();
        selectedOption = options[0];
    }

    private void InitializeRulesetData()
    {
        ruleNamespaces.Clear();

        string targetLabel = "RoslynAnalyzer";

        string[] allAssetGUIDs = AssetDatabase.FindAssets(string.Empty);

        // 解析选中的RuleSet文件
        if (!string.IsNullOrEmpty(selectedOption))
        {
            RuleSetParser parser = new RuleSetParser();
            parser.ParseRuleSet(selectedOption);
            ruleSetRules = parser.GetRuleSetRules();
        }
        else
        {
            ruleSetRules.Clear();
        }

        foreach (string assetGUID in allAssetGUIDs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
            if (assetPath.EndsWith(".dll"))
            {
                string[] labels = AssetDatabase.GetLabels(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
                if (Array.Exists(labels, label => label == targetLabel))
                {
                    try
                    {
                        LoadAnalyzer(assetPath);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Cannot load analyzer {assetPath}: {e}");
                    }
                }
            }
        }
    }

    private void LoadAnalyzer(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        var assemblyName = assembly.GetName().Name;
        var diagnosticAnalyzerTypes = assembly.GetTypes()
                                            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract)
                                            .ToList();
        Dictionary<string, RuleNamespace> namespaceTable = new Dictionary<string, RuleNamespace>();
        foreach (var type in diagnosticAnalyzerTypes)
        {
            var analyzerInstance = Activator.CreateInstance(type) as DiagnosticAnalyzer;
            if (analyzerInstance != null)
            {
                var ruleNamespace = type.Namespace;
                System.Collections.Immutable.ImmutableArray<DiagnosticDescriptor> supportedDiagnostics = analyzerInstance.SupportedDiagnostics;
                foreach (var diagnostic in supportedDiagnostics)
                {
                    if (!namespaceTable.ContainsKey(ruleNamespace))
                    {
                        namespaceTable.Add(ruleNamespace, new RuleNamespace { name = ruleNamespace, analyzerId = assemblyName });
                    }

                    var ruleNamespaceEntry = namespaceTable[ruleNamespace];
                    RuleEntry ruleEntry = new RuleEntry
                    {
                        enabled = diagnostic.IsEnabledByDefault,
                        id = diagnostic.Id,
                        title = diagnostic.Title.ToString(),
                        severity = diagnostic.DefaultSeverity,
                    };

                    // 检查RuleSet中的规则
                    if (ruleSetRules.TryGetValue(ruleNamespace, out var rules) && rules.TryGetValue(ruleEntry.id, out var action))
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

                    ruleNamespaceEntry.entries.Add(ruleEntry);
                }
            }
        }

        ruleNamespaces.AddRange(namespaceTable.Values);
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawContent();
        DrawBottomButtons(); // 添加底部按钮的绘制
    }

    private void DrawBottomButtons()
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace(); // 将按钮推到右边

        if (GUILayout.Button("Revert", GUILayout.Width(80)))
        {
            RevertToDefault();
        }

        if (GUILayout.Button("Apply", GUILayout.Width(80)))
        {
            ApplyChanges();
        }

        GUILayout.EndHorizontal();
    }

    private void RevertToDefault()
    {
        InitializeRulesetData();
        Repaint();
    }

    private void ApplyChanges()
    {
        if (!string.IsNullOrEmpty(selectedOption) && selectedOption != "Create New Ruleset")
        {
            string filePath = selectedOption;
            RuleSetParser parser = new RuleSetParser();
            parser.SaveRuleSet(filePath, ruleNamespaces);
            Debug.Log("保存成功： " + filePath);
        }
        else
        {
            Debug.Log("请先选择一个规则集文件");
        }
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
            CreateNewRuleset(preSelected);
        }
        else if (preSelected != selectedOption)
        {
            InitializeRulesetData();
        }

        // 绘制分隔线
        GUILayout.FlexibleSpace();
        GUILayout.Label("Search:");

        // 搜索栏
        searchString = GUILayout.TextField(searchString, GUILayout.Width(150));

        GUILayout.EndHorizontal();
    }

    private string searchString = string.Empty;

    private void CreateNewRuleset(string preSelected)
    {
        string path = EditorUtility.SaveFilePanel(
            "Create New Ruleset",
            string.Empty,
            "Default",
            "ruleset");

        if (!string.IsNullOrEmpty(path))
        {
            // 创建并保存新的规则集文件
            // 这里可以添加创建规则集文件的逻辑
            Debug.Log("创建了新的规则集文件：" + path);
            options = options.Append(path).OrderBy(x => x).ToArray();
            selectedOption = string.Empty;
            ruleNamespaces.Clear();
            InitializeRulesetData();

            selectedOption = path;
            ApplyChanges();
        }
        else
        {
            selectedOption = preSelected;
        }
    }

    private void DrawContent()
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        foreach (var ruleNamespace in ruleNamespaces)
        {
            bool hasMatchingRules = ruleNamespace.entries.Any(e =>
                e.id.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                e.title.Contains(searchString, StringComparison.OrdinalIgnoreCase));

            if (hasMatchingRules)
            {
                if (!namespaceFoldouts.ContainsKey(ruleNamespace.name))
                {
                    namespaceFoldouts[ruleNamespace.name] = false;
                }

                if (searchString != string.Empty)
                {
                    namespaceFoldouts[ruleNamespace.name] = true;
                }

                bool isExpanded = namespaceFoldouts[ruleNamespace.name];
                bool allEnabled = ruleNamespace.entries.Count == 0 ? false : ruleNamespace.entries.All(e => e.enabled);
                bool someEnabled = !allEnabled && ruleNamespace.entries.Any(e => e.enabled);

                EditorGUILayout.BeginHorizontal(EditorStyles.foldoutHeader);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = someEnabled;
                Rect toggleRect = EditorGUILayout.GetControlRect(GUILayout.Width(20));
                bool newToggle = EditorGUI.Toggle(toggleRect, allEnabled);
                EditorGUI.showMixedValue = false;

                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var entry in ruleNamespace.entries)
                    {
                        entry.enabled = newToggle;
                    }
                }

                GUILayout.Label(ruleNamespace.name, GUILayout.ExpandWidth(true));

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
                        namespaceFoldouts[ruleNamespace.name] = !isExpanded;
                        Event.current.Use();
                    }
                }

                if (namespaceFoldouts[ruleNamespace.name])
                {
                    EditorGUI.indentLevel++;
                    foreach (var entry in ruleNamespace.entries)
                    {
                        if (entry.id.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                            entry.title.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                        {
                            GUILayout.BeginHorizontal();
                            entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(40));
                            GUILayout.Label(entry.id, GUILayout.Width(60), GUILayout.ExpandWidth(false));
                            GUILayout.Label(entry.title, GUILayout.ExpandWidth(true));
                            entry.severity = (DiagnosticSeverity)EditorGUILayout.EnumPopup(
                                entry.severity,
                                GUILayout.Width(80),
                                GUILayout.ExpandWidth(false));
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

    public void SaveRuleSet(string filePath, List<RuleNamespace> ruleNamespaces)
    {
        XmlDocument xmlDoc = new XmlDocument();
        XmlElement root = xmlDoc.CreateElement("RuleSet");
        root.SetAttribute("ToolsVersion", "17.0");
        root.SetAttribute("Name", "Rules");
        root.SetAttribute("Description", "Rules");
        xmlDoc.AppendChild(newChild: root);

        foreach (var namespaceItem in ruleNamespaces)
        {
            XmlElement rulesElement = xmlDoc.CreateElement("Rules");
            rulesElement.SetAttribute("AnalyzerId", namespaceItem.analyzerId);
            rulesElement.SetAttribute("RuleNamespace", namespaceItem.name);

            foreach (var entry in namespaceItem.entries)
            {
                XmlElement ruleElement = xmlDoc.CreateElement("Rule");
                ruleElement.SetAttribute("Id", entry.id);
                ruleElement.SetAttribute("Action", entry.enabled ? entry.severity.ToString() : "None");
                rulesElement.AppendChild(ruleElement);
            }

            root.AppendChild(rulesElement);
        }

        xmlDoc.Save(filePath);
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

[System.Serializable]
public class RuleNamespace
{
    public string name;

    public string analyzerId;
    public List<RuleEntry> entries = new List<RuleEntry>();
}

// 定义数据结构
[System.Serializable]
public class RuleEntry
{
    public bool enabled;
    public string id;
    public string title;
    public DiagnosticSeverity severity;
}