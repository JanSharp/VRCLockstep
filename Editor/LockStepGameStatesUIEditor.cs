using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using UnityEditor;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Collections.ObjectModel;
using System;

namespace JanSharp
{
    [InitializeOnLoad]
    public static class LockStepGameStatesUIEditorOnBuild
    {
        static LockStepGameStatesUIEditorOnBuild() => JanSharp.OnBuildUtil.RegisterType<LockStepGameStatesUI>(OnBuild, 2);

        private static bool OnBuild(LockStepGameStatesUI gameStatesUI)
        {
            SerializedObject proxy = new SerializedObject(gameStatesUI);
            LockStep lockStep = (LockStep)proxy.FindProperty("lockStep").objectReferenceValue;
            if (lockStep == null)
            {
                Debug.LogError("[LockStep] The Lock Step Game State UI requires an instance of the "
                    + "Lock Step script in the scene.", gameStatesUI);
                return false;
            }

            GameObject mainGSEntryPrefab = (GameObject)proxy.FindProperty("mainGSEntryPrefab").objectReferenceValue;
            GameObject importGSEntryPrefab = (GameObject)proxy.FindProperty("importGSEntryPrefab").objectReferenceValue;
            GameObject exportGSEntryPrefab = (GameObject)proxy.FindProperty("exportGSEntryPrefab").objectReferenceValue;
            Transform mainGSList = (Transform)proxy.FindProperty("mainGSList").objectReferenceValue;
            Transform importGSList = (Transform)proxy.FindProperty("importGSList").objectReferenceValue;
            Transform exportGSList = (Transform)proxy.FindProperty("exportGSList").objectReferenceValue;
            if (mainGSEntryPrefab == null || importGSEntryPrefab == null || exportGSEntryPrefab == null
                || mainGSList == null || importGSList == null || exportGSList == null)
            {
                Debug.LogError("[LockStep] The Lock Step Game State UI is missing internal references.", gameStatesUI);
                return false;
            }

            var allGameStates = LockStepOnBuild.AllGameStates;
            PopulateList<LockStepMainGSEntry>(
                allGameStates: allGameStates,
                proxy: proxy,
                entriesArrayName: "mainGSEntries",
                postfix: " (MainGSEntry)",
                list: mainGSList,
                prefab: mainGSEntryPrefab);
            PopulateList<LockStepImportGSEntry>(
                allGameStates: allGameStates,
                proxy: proxy,
                entriesArrayName: "importGSEntries",
                postfix: " (ImportGSEntry)",
                list: importGSList,
                prefab: importGSEntryPrefab,
                leaveInteractableUnchanged: true,
                callback: (inst, gs) => {
                    SerializedObject instProxy = new SerializedObject(inst);
                    instProxy.FindProperty("gameState").objectReferenceValue = gs;
                    instProxy.ApplyModifiedProperties();

                    SerializedObject infoLabelProxy = new SerializedObject(inst.infoLabel);
                    infoLabelProxy.FindProperty("m_text").stringValue = gs.GameStateSupportsImportExport
                        ? ""
                        : "does not support import/export";
                    infoLabelProxy.ApplyModifiedProperties();
                });
            PopulateList<LockStepExportGSEntry>(
                allGameStates: allGameStates,
                proxy: proxy,
                entriesArrayName: "exportGSEntries",
                postfix: " (ExportGSEntry)",
                list: exportGSList,
                prefab: exportGSEntryPrefab,
                callback: (inst, gs) => {
                    SerializedObject instProxy = new SerializedObject(inst);
                    instProxy.FindProperty("gameStatesUI").objectReferenceValue = gameStatesUI;
                    instProxy.FindProperty("gameState").objectReferenceValue = gs;
                    instProxy.ApplyModifiedProperties();

                    SerializedObject infoLabelProxy = new SerializedObject(inst.infoLabel);
                    infoLabelProxy.FindProperty("m_text").stringValue = gs.GameStateSupportsImportExport
                        ? "autosave"
                        : "does not support import/export";
                    infoLabelProxy.ApplyModifiedProperties();

                    SerializedObject infoObjProxy = new SerializedObject(inst.infoLabel.gameObject);
                    infoObjProxy.FindProperty("m_IsActive").boolValue = !gs.GameStateSupportsImportExport;
                    infoObjProxy.ApplyModifiedProperties();
                });

            Slider autosaveIntervalSlider = (Slider)proxy.FindProperty("autosaveIntervalSlider").objectReferenceValue;
            int defaultAutosaveInterval = (int)autosaveIntervalSlider.value;
            proxy.FindProperty("minAutosaveInterval").intValue = (int)autosaveIntervalSlider.minValue;
            proxy.FindProperty("defaultAutosaveInterval").intValue = defaultAutosaveInterval;
            proxy.FindProperty("autosaveInterval").intValue = defaultAutosaveInterval;
            proxy.ApplyModifiedProperties();

            InputField autosaveIntervalField = (InputField)proxy.FindProperty("autosaveIntervalField").objectReferenceValue;
            SerializedObject fieldProxy = new SerializedObject(autosaveIntervalField);
            fieldProxy.FindProperty("m_Text").stringValue = defaultAutosaveInterval.ToString();
            fieldProxy.ApplyModifiedProperties();

            return true;
        }

        private static void PopulateList<T>(
            ReadOnlyCollection<LockStepGameState> allGameStates,
            SerializedObject proxy,
            string entriesArrayName,
            string postfix,
            Transform list,
            GameObject prefab,
            bool leaveInteractableUnchanged = false,
            Action<T, LockStepGameState> callback = null)
            where T : LockStepGameStateEntryBase
        {
            T[] entries = new T[allGameStates.Count];
            for (int i = 0; i < allGameStates.Count; i++)
            {
                GameObject inst;
                if (i < list.childCount)
                    inst = list.GetChild(i).gameObject;
                else
                {
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.transform.SetParent(list, false);
                    Undo.RegisterCreatedObjectUndo(inst, "LockStepGameStatesUI entry creation OnBuild");
                }
                SerializedObject instProxy = new SerializedObject(inst);
                instProxy.FindProperty("m_Name").stringValue = allGameStates[i].GameStateInternalName + postfix;
                instProxy.ApplyModifiedProperties();

                T entry = inst.GetComponent<T>();
                entries[i] = entry;

                SerializedObject displayNameTextProxy = new SerializedObject(entry.displayNameText);
                displayNameTextProxy.FindProperty("m_text").stringValue = allGameStates[i].GameStateDisplayName;
                displayNameTextProxy.ApplyModifiedProperties();

                if (entry.toggledImage != null)
                {
                    SerializedObject toggledImageProxy = new SerializedObject(entry.toggledImage);
                    toggledImageProxy.FindProperty("m_Color").colorValue = allGameStates[i].GameStateSupportsImportExport
                        ? entry.goodColor
                        : entry.badColor;
                    toggledImageProxy.ApplyModifiedProperties();

                    SerializedObject mainToggleProxy = new SerializedObject(entry.mainToggle);
                    if (!leaveInteractableUnchanged)
                        mainToggleProxy.FindProperty("m_Interactable").boolValue = allGameStates[i].GameStateSupportsImportExport;
                    mainToggleProxy.FindProperty("m_IsOn").boolValue = !allGameStates[i].GameStateSupportsImportExport;
                    mainToggleProxy.ApplyModifiedProperties();
                }
                if (callback != null)
                    callback(entry, allGameStates[i]);
            }

            while (list.childCount > allGameStates.Count)
                Undo.DestroyObjectImmediate(list.GetChild(list.childCount - 1).gameObject);

            EditorUtil.SetArrayProperty(proxy.FindProperty(entriesArrayName), entries, (p, v) => p.objectReferenceValue = v);
        }
    }
}
