using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;

public class CreateHLSLFileEditor
{
    [MenuItem("Assets/Create/Shader/Custom Function (HLSL)")]
    static void CreateCustomFunction()
    {
        var templatePath = Path.GetFullPath("Packages/com.zwcmc.tiny-rp/Editor/ShaderTool/HLSLTemplate.hlsl");
        ProjectWindowUtil.CreateScriptAssetFromTemplateFile(templatePath, "New Function.hlsl");
    }
}

public class HLSLFileReplace : AssetModificationProcessor
{
    public static void OnWillCreateAsset(string assetName)
    {
        string newFilePath = assetName.Replace(".meta", "");
        string fileExt = Path.GetExtension(newFilePath);
        if (fileExt != ".hlsl") return;
        string realPath = Application.dataPath.Replace("Assets", "") + newFilePath;
        if (File.Exists(realPath))
        {
            string fileContent = File.ReadAllText(realPath);
            string fileName = Path.GetFileNameWithoutExtension(realPath);
            var pattern = new Regex("[A-Z][a-z]*");
            string includedName = "";
            foreach(var m in pattern.Matches(fileName))
            {
                if (includedName != "")
                    includedName = includedName + "_" + m.ToString().ToUpper();
                else
                    includedName = m.ToString().ToUpper();
            }
            fileContent = fileContent.Replace("#HLSLCLASSNAME#", includedName);
            File.WriteAllText(realPath, fileContent);
        }
    }
}
