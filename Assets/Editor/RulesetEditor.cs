using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

public class RulesetEditor : EditorWindow
{
    [MenuItem( itemName: "Tools/Rulesets" )]
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    static void Rulesets()
    {
        Debug.Log("ruleset");
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
                    Debug.Log($"找到符合标签的资源: {assetPath}");
                    if (!assetPath.Contains("CodeFixes"))
                    {
                        LoadAnalyzer(assetPath);
                    }
                }
            }
        }
    }

    static void LoadAnalyzer(string path)
    {
        
        // 加载 Roslynator 的分析器 DLL
        var assembly = Assembly.LoadFrom(path);

        // 获取所有实现 DiagnosticAnalyzer 的类型
        var diagnosticAnalyzerTypes = assembly.GetTypes()
                                              .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract)
                                              .ToList();

        // 打印每个规则的名字
        foreach (var type in diagnosticAnalyzerTypes)
        {
            Debug.Log($"Found rule: {type.FullName}");

            // 如果你需要更多的规则信息，可以在这里调用相关方法
            var analyzerInstance = Activator.CreateInstance(type) as DiagnosticAnalyzer;
            if (analyzerInstance != null) 
            {
                System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.DiagnosticDescriptor> supportedDiagnostics = analyzerInstance.SupportedDiagnostics;
                foreach (var diagnostic in supportedDiagnostics)
                {
                    Debug.Log($"  - Diagnostic ID: {diagnostic.Id}, Title: {diagnostic.Title}");
                }
            }
        }
    }
}
