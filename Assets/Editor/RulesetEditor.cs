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
        // 初始化示例数据
        InitializeSampleData();
    }

    private void InitializeSampleData()
    {
        categories.Clear();

        // 设置要查找的标签名称
        string targetLabel = "RoslynAnalyzer"; // 替换为你要查找的标签

        // 1. 查找项目中所有的资源
        string[] allAssetGUIDs = AssetDatabase.FindAssets(""); // 查找所有资源
        
        // 2. 遍历所有资源
        foreach (string assetGUID in allAssetGUIDs)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(assetGUID);
            // 只处理 .dll 文件
            if (assetPath.EndsWith(".dll"))
            {
                // 获取该资源的标签
                string[] labels = AssetDatabase.GetLabels(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
                
                // 3. 如果资源打上了目标标签
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
        
        // 加载 Roslynator 的分析器 DLL
        var assembly = Assembly.LoadFrom(path);

        // 获取所有实现 DiagnosticAnalyzer 的类型
        var diagnosticAnalyzerTypes = assembly.GetTypes()
                                              .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract)
                                              .ToList();
        Dictionary<string, Category> categoryTable = new Dictionary<string, Category>();
        // 打印每个规则的名字
        foreach (var type in diagnosticAnalyzerTypes)
        {
            // 如果你需要更多的规则信息，可以在这里调用相关方法
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
        GUILayout.Label("Rulesets", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

private void DrawContent()
{
    scrollPosition = GUILayout.BeginScrollView(scrollPosition);

    foreach (var category in categories)
    {
        if (!categoryFoldouts.ContainsKey(category.name))
        {
            categoryFoldouts[category.name] = false;
        }

        bool isExpanded = categoryFoldouts[category.name];
        bool allEnabled = category.entries.Count == 0 ? false : category.entries.All(e => e.enabled);
        bool someEnabled = !allEnabled && category.entries.Any(e => e.enabled);

        // 开始绘制自定义折叠头
        EditorGUILayout.BeginHorizontal(EditorStyles.foldoutHeader);

        // 绘制Toggle控件
        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = someEnabled;
        Rect toggleRect = EditorGUILayout.GetControlRect(GUILayout.Width(20));
        bool newToggle = EditorGUI.Toggle(toggleRect, allEnabled);
        EditorGUI.showMixedValue = false;

        // 更新所有entry的启用状态
        if (EditorGUI.EndChangeCheck())
        {
            foreach (var entry in category.entries)
            {
                entry.enabled = newToggle;
            }
        }

        // 绘制分类名称标签
        GUILayout.Label(category.name, GUILayout.ExpandWidth(true));

        // 绘制折叠箭头图标
        GUIContent arrowIcon = isExpanded 
            ? EditorGUIUtility.IconContent("IN foldout on") 
            : EditorGUIUtility.IconContent("IN foldout");
        GUILayout.Label(arrowIcon, GUILayout.Width(20));

        EditorGUILayout.EndHorizontal();

        // 处理折叠头的点击事件
        Rect headerRect = GUILayoutUtility.GetLastRect();
        if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
        {
            // 排除toggle区域的点击
            if (!toggleRect.Contains(Event.current.mousePosition))
            {
                categoryFoldouts[category.name] = !isExpanded;
                Event.current.Use();
            }
        }

        // 展开时绘制详细规则列表
        if (categoryFoldouts[category.name])
        {
            EditorGUI.indentLevel++;
            foreach (var entry in category.entries)
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
            EditorGUI.indentLevel--;
        }
    }

    GUILayout.EndScrollView();
}
}
