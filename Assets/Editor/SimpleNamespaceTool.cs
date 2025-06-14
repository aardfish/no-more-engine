using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

public class SimpleNamespaceTool : EditorWindow
{
    private string scriptsFolderPath = "Assets/No More Engine";
    private Vector2 scrollPosition;
    
    [MenuItem("Tools/No More Engine/Simple Namespace Tool")]
    public static void ShowWindow()
    {
        var window = GetWindow<SimpleNamespaceTool>("Simple Namespace Tool");
        window.minSize = new Vector2(600, 400);
    }
    
    void OnGUI()
    {
        GUILayout.Label("Simple Namespace Tool", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        if (GUILayout.Button("Add Namespaces Based on Folder Structure", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Confirm", 
                "This will add namespaces to all C# files based on their folder structure.\n\n" +
                "Backups will be created.\n\nContinue?", 
                "Yes", "Cancel"))
            {
                ProcessAllFiles();
            }
        }
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Revert All From Backups", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Confirm", 
                "This will restore all files from their .backup versions.\n\nContinue?", 
                "Yes", "Cancel"))
            {
                RevertAllBackups();
            }
        }
        
        GUILayout.Space(20);
        GUILayout.Label("Instructions:", EditorStyles.boldLabel);
        GUILayout.Label("1. This tool adds namespaces based on folder structure");
        GUILayout.Label("2. Example: /Input/InputSerializer.cs gets NoMoreEngine.Input");
        GUILayout.Label("3. Backups are created as .backup files");
        GUILayout.Label("4. After running, manually add using statements as needed");
    }
    
    private void ProcessAllFiles()
    {
        if (!Directory.Exists(scriptsFolderPath))
        {
            EditorUtility.DisplayDialog("Error", "Scripts folder not found: " + scriptsFolderPath, "OK");
            return;
        }
        
        var files = Directory.GetFiles(scriptsFolderPath, "*.cs", SearchOption.AllDirectories);
        int processed = 0;
        int skipped = 0;
        int errors = 0;
        
        foreach (var file in files)
        {
            try
            {
                if (FileHasNamespace(file))
                {
                    skipped++;
                    continue;
                }
                
                string namespaceName = GetNamespaceFromPath(file);
                AddNamespaceToFile(file, namespaceName);
                processed++;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error processing " + file + ": " + e.Message);
                errors++;
            }
        }
        
        AssetDatabase.Refresh();
        
        string message = string.Format(
            "Complete!\n\nProcessed: {0}\nSkipped: {1}\nErrors: {2}", 
            processed, skipped, errors);
            
        EditorUtility.DisplayDialog("Done", message, "OK");
    }
    
    private bool FileHasNamespace(string filePath)
    {
        string content = File.ReadAllText(filePath);
        return content.Contains("namespace ");
    }
    
    private string GetNamespaceFromPath(string filePath)
    {
        string path = filePath.Replace('\\', '/');
        
        int startIndex = path.IndexOf("/No More Engine/");
        if (startIndex == -1) return "NoMoreEngine";
        
        startIndex += "/No More Engine/".Length;
        
        int endIndex = path.LastIndexOf('/');
        if (endIndex <= startIndex) return "NoMoreEngine";
        
        string folderPath = path.Substring(startIndex, endIndex - startIndex);
        
        if (string.IsNullOrEmpty(folderPath)) return "NoMoreEngine";
        
        string[] parts = folderPath.Split('/');
        return "NoMoreEngine." + string.Join(".", parts);
    }
    
    private void AddNamespaceToFile(string filePath, string namespaceName)
    {
        string content = File.ReadAllText(filePath);
        
        // Create backup
        File.WriteAllText(filePath + ".backup", content);
        
        // Find the end of using statements
        int insertIndex = 0;
        string[] lines = content.Split('\n');
        
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("using ") || string.IsNullOrWhiteSpace(line))
            {
                insertIndex = i + 1;
            }
            else
            {
                break;
            }
        }
        
        // Build new content
        List<string> newLines = new List<string>();
        
        // Add existing usings
        for (int i = 0; i < insertIndex; i++)
        {
            newLines.Add(lines[i]);
        }
        
        // Add namespace
        newLines.Add("");
        newLines.Add("namespace " + namespaceName);
        newLines.Add("{");
        
        // Add remaining content with indentation
        for (int i = insertIndex; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                newLines.Add("    " + lines[i]);
            }
            else
            {
                newLines.Add("");
            }
        }
        
        // Close namespace
        newLines.Add("}");
        
        // Write file
        File.WriteAllText(filePath, string.Join("\n", newLines));
        
        Debug.Log("Added namespace " + namespaceName + " to " + Path.GetFileName(filePath));
    }
    
    private void RevertAllBackups()
    {
        var backupFiles = Directory.GetFiles(scriptsFolderPath, "*.backup", SearchOption.AllDirectories);
        
        if (backupFiles.Length == 0)
        {
            EditorUtility.DisplayDialog("No Backups", "No backup files found.", "OK");
            return;
        }
        
        int reverted = 0;
        
        foreach (var backup in backupFiles)
        {
            try
            {
                string originalPath = backup.Replace(".backup", "");
                File.Copy(backup, originalPath, true);
                File.Delete(backup);
                reverted++;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error reverting " + backup + ": " + e.Message);
            }
        }
        
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("Done", "Reverted " + reverted + " files.", "OK");
    }
}