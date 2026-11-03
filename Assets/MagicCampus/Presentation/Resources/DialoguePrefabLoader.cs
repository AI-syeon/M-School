using System;
using MagicCampus.Presentation.Dialogue;
using UnityEngine;

namespace MagicCampus.Presentation.Prefabs
{
    /// <summary>Loads the reusable Resources/MagicCampus/Dialogue.prefab. A missing prefab is an explicit error.</summary>
    public static class DialoguePrefabLoader
    {
        public static GameObject LoadPrefab()
        {
            var prefab = UnityEngine.Resources.Load<GameObject>(DialoguePresentation.ResourcePath);
            if (prefab == null)
                throw new InvalidOperationException("Dialogue prefab not found at Resources/" + DialoguePresentation.ResourcePath);
            if (prefab.GetComponent<DialoguePresentation>() == null)
                throw new InvalidOperationException("Dialogue prefab has no DialoguePresentation component");
            return prefab;
        }

        /// <summary>Instantiates the prefab. The caller must still call DialoguePresentation.Configure.</summary>
        public static DialoguePresentation Instantiate(Transform parent)
        {
            var go = UnityEngine.Object.Instantiate(LoadPrefab(), parent);
            return go.GetComponent<DialoguePresentation>();
        }
    }
}
