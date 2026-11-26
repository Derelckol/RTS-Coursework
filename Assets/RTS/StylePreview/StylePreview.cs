using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rts
{
    public sealed class StylePreview : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private MatchVisibility fog;
        [SerializeField] private CombatTarget[] models;
        [SerializeField] private Collider[] landmarks;
        [SerializeField] private GameObject selection;

        private readonly List<CombatTarget> observers = new List<CombatTarget>();

        private void Start()
        {
            observers.AddRange(Array.FindAll(models, actor => actor.Side == Side.Opponent));
            fog.Initialize(new RectInt(-40, -30, 80, 60), observers, landmarks);
            AssertVisibility(false, true);
            observers.Clear();
            observers.AddRange(models);
            fog.Refresh();
            AssertVisibility(true, true);
            selection.SetActive(true);
            Debug.Log("STYLE PREVIEW: all child renderers hide and return through MatchVisibility; selection enabled.");
        }

        private void AssertVisibility(bool expected, bool checkLandmarks)
        {
            foreach (var actor in models)
            {
                if (actor.Side != Side.Opponent) continue;
                foreach (var part in actor.GetComponentsInChildren<Renderer>())
                    if (part.enabled != expected) throw new InvalidOperationException("Incorrect fog visibility: " + part.name);
            }
            if (!checkLandmarks) return;
            foreach (var landmark in landmarks)
                foreach (var part in landmark.GetComponentsInChildren<Renderer>())
                    if (part.enabled != expected) throw new InvalidOperationException("Incorrect landmark visibility: " + part.name);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.digit1Key.wasPressedThisFrame) view.orthographicSize = 6;
            if (keyboard.digit2Key.wasPressedThisFrame) view.orthographicSize = 10;
            if (keyboard.digit3Key.wasPressedThisFrame) view.orthographicSize = 16;
            if (keyboard.sKey.wasPressedThisFrame) selection.SetActive(!selection.activeSelf);
            if (keyboard.fKey.wasPressedThisFrame)
            {
                bool reveal = observers.Count < models.Length;
                observers.Clear();
                observers.AddRange(reveal ? models : Array.FindAll(models, actor => actor.Side == Side.Opponent));
                fog.Refresh();
                AssertVisibility(reveal, false);
            }
        }

        public void SetZoom(float size) => view.orthographicSize = size;
    }
}

