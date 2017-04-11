using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MissingScriptFix
{
    [MenuItem("Assets/Fix Missing Scripts")]
    public static void FixMissingScripts()
    {
        string[] allAssets = GetAllPrefabs();

        for (int i = 0; i < allAssets.Length; i++)
        {
            EditorUtility.DisplayProgressBar("Fix Missing Scripts", i + "/" + allAssets.Length + " " + Path.GetFileName(allAssets[i]), (float)i / allAssets.Length);

            RemoveMissing(allAssets[i]);
        }

        EditorUtility.ClearProgressBar();

        AssetDatabase.Refresh();

    }


    public static void RemoveMissing(string path)
    {
        Object obj = AssetDatabase.LoadMainAssetAtPath(path);

        GameObject go = null;

        try
        {
            go = (GameObject) obj;
        }
        catch (Exception e)
        {
            Debug.Log("failed to convert!   " + path);
        }

        if (go == null)
        {
            return;
        }

        bool hasMissingComponents = false;

        List<GameObject> allGameObjects = new List<GameObject>();

        allGameObjects.Add(go);

        AddChildren(go, ref allGameObjects);


        foreach (var currentObject in allGameObjects)
        {
            if (hasMissingComponents)
            {
                break;
            }

            Component[] currentComponents = currentObject.GetComponentsInChildren<Component>(true);

            foreach (var c in currentComponents)
            {
                if (c == null)
                {
                    hasMissingComponents = true;
                    break;
                }
            }

        }

        if (!hasMissingComponents)
        {
            return;
        }

        StringBuilder resultYaml = new StringBuilder();

        List<string> idsToRemove = new List<string>();

        Dictionary<string, List<string>> gameObjectComponents = new Dictionary<string, List<string>>();

        using (StreamReader reader = File.OpenText(path))
        {
            string currentLine;

            //create game object components model
            while (!reader.EndOfStream)
            {
                currentLine = reader.ReadLine();

                if (currentLine != null && currentLine.Contains("--- !u!1 &")) // Game object document header!
                {
                    string key = currentLine.Replace("--- !u!1 &", ""); // game object local identifier in yaml

                    List<string> componentIds = new List<string>();

                    while (currentLine != null && !currentLine.Contains("m_Component"))
                    {
                        currentLine = reader.ReadLine();
                    }

                    currentLine = reader.ReadLine(); // start collecting components

                    while (currentLine != null && currentLine.Contains("component"))
                    {
                        componentIds.Add(Regex.Match(currentLine, @"\d+").Value);
                        currentLine = reader.ReadLine();
                    }

                    if (gameObjectComponents.ContainsKey(key))
                    {
                        Debug.LogError("Duplicate local ids for game obects found in " + go.name);
                    }
                    else
                    {
                        gameObjectComponents.Add(key, componentIds);
                    }
                }
            }
            //Now we have all game objects and its components from yaml

            //Filter components so dictionary will contain only missing components ids
            foreach (var currentObject in allGameObjects)
            {
                string objLocalID = ObjectLocalID(currentObject);

                if (gameObjectComponents.ContainsKey(objLocalID))
                {
                    List<string> componentIds = gameObjectComponents[objLocalID];

                    Component[] currentComponents = currentObject.GetComponentsInChildren<Component>(true);

                    foreach (var c in currentComponents)
                    {
                        if (c != null)
                        {
                            componentIds.Remove(ObjectLocalID(c));
                        }
                    }
                }
            }

            foreach (var pair in gameObjectComponents)
            {
                foreach (var componentId in pair.Value)
                {
                    idsToRemove.Add(componentId);
                }
            }

            //return stream position to start
            //now we can write new yaml excluding missing components
            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();

            bool needToSkip = false;

            bool needSkipCurrentLine = false;

            while (!reader.EndOfStream)
            {
                currentLine = reader.ReadLine();

                if (currentLine != null )
                {
                    foreach (var remID in idsToRemove)
                    {
                        if (currentLine.Contains(remID))
                        {
                            needSkipCurrentLine = true;
                        }
                    }

                    if (currentLine.StartsWith("--- !u!"))
                    {
                        needToSkip = needSkipCurrentLine;
                    }
                }

                if (!needToSkip && ! needSkipCurrentLine)
                {
                    resultYaml.AppendLine(currentLine);
                }

                needSkipCurrentLine = false;
            }
        }

        File.WriteAllText(path, resultYaml.ToString());

        EditorUtility.SetDirty(obj);
    }

    public static void AddChildren(GameObject gameObject, ref List<GameObject> children)
    {
        for (int i = 0; i < gameObject.transform.childCount; i++)
        {
            GameObject c = gameObject.transform.GetChild(i).gameObject;
            children.Add(c);
            AddChildren(c, ref children);
        }
    }

    public static string ObjectLocalID(Object obj)
    {
        PropertyInfo inspectorModeInfo =
            typeof(SerializedObject).GetProperty("inspectorMode", BindingFlags.NonPublic | BindingFlags.Instance);

        SerializedObject serializedObject = new SerializedObject(obj);
        inspectorModeInfo.SetValue(serializedObject, InspectorMode.Debug, null);

        SerializedProperty localIdProp =
            serializedObject.FindProperty("m_LocalIdentfierInFile");   //note the misspelling!

        return localIdProp.longValue.ToString();
    }

    public static string[] GetAllPrefabs ()
    {
        string[] temp = AssetDatabase.GetAllAssetPaths();
        List<string> result = new List<string>();
        foreach ( string s in temp ) {
            if ( s.Contains( ".prefab" ) ) result.Add( s );
        }
        return result.ToArray();
    }
}
