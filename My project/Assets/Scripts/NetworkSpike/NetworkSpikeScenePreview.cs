using System;
using System.Collections.Generic;
using UnityEngine;

namespace BatteryRushArena.NetworkSpike
{
    /// <summary>
    /// Keeps a deterministic arena preview visible in the scene while not playing.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class NetworkSpikeScenePreview : MonoBehaviour
    {
        private const float ArenaWorldHalfExtent = 7.25f;
        private static readonly Vector2[] TrapPreviewPositions =
        {
            new(-4.6f, 2.8f),
            new(-1.9f, -4.1f),
            new(2.2f, 4.3f),
            new(4.7f, -2.7f)
        };

        private static readonly Vector2[] BatteryPreviewPositions =
        {
            new(-4.85f, 3.9f),
            new(-2.35f, 5.15f),
            new(2.8f, 5.45f),
            new(5.2f, 2.9f),
            new(5.0f, -3.15f),
            new(2.1f, -5.35f),
            new(-2.95f, -5.1f),
            new(-5.35f, -2.4f)
        };

        private static readonly PreviewActorState[] PreviewActors =
        {
            new("PreviewHost-Actor", new Vector2(-5.1f, 0f), new Color(0.33f, 0.9f, 0.53f, 1f)),
            new("PreviewGuest-Actor", new Vector2(5.1f, 0f), new Color(0.33f, 0.7f, 1f, 1f))
        };

        [SerializeField] private string previewPlayerName = "Player0000";

        private static Sprite solidSprite;

        public string PreviewPlayerName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(previewPlayerName) &&
                    !string.Equals(previewPlayerName, "Player0000", StringComparison.Ordinal))
                {
                    return previewPlayerName;
                }

                return FormattableString.Invariant($"Player{System.Diagnostics.Process.GetCurrentProcess().Id % 10000:0000}");
            }
        }

        private void OnEnable()
        {
            RefreshPreview();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                RefreshPreview();
            }
            else
            {
                SetPreviewActorsVisible(false);
            }
        }

        private void RefreshPreview()
        {
            if (Application.isPlaying)
            {
                SetPreviewActorsVisible(false);
                return;
            }

            EnsureArenaSurface();
            EnsureTrapMarkers();
            EnsureBatteryMarkers();
            RemoveStaleRuntimeActors();
            EnsurePreviewActors();
        }

        private void EnsureArenaSurface()
        {
            EnsureSprite(
                "ArenaSurface",
                transform,
                Vector2.zero,
                new Vector3((ArenaWorldHalfExtent * 2f) + 0.8f, (ArenaWorldHalfExtent * 2f) + 0.8f, 1f),
                new Color(0.06f, 0.08f, 0.12f, 0.92f),
                -10);
        }

        private void EnsureTrapMarkers()
        {
            for (var index = 0; index < TrapPreviewPositions.Length; index++)
            {
                EnsureSprite(
                    $"Trap-{index + 1}",
                    transform,
                    TrapPreviewPositions[index],
                    new Vector3(0.9f, 0.9f, 1f),
                    new Color(0.8f, 0.24f, 0.28f, 0.35f),
                    5);
            }
        }

        private void EnsureBatteryMarkers()
        {
            for (var index = 0; index < BatteryPreviewPositions.Length; index++)
            {
                var renderer = EnsureSprite(
                    $"Battery-{index + 1}",
                    transform,
                    BatteryPreviewPositions[index],
                    new Vector3(0.35f, 0.35f, 1f),
                    new Color(1f, 0.83f, 0.18f, 1f),
                    20);
                renderer.gameObject.SetActive(true);
            }
        }

        private void RemoveStaleRuntimeActors()
        {
            var stale = new List<GameObject>();
            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index);
                if (child.name.EndsWith("-Actor", StringComparison.Ordinal))
                {
                    stale.Add(child.gameObject);
                }
            }

            foreach (var gameObject in stale)
            {
                if (gameObject != null)
                {
                    DestroyImmediate(gameObject);
                }
            }
        }

        private void SetPreviewActorsVisible(bool isVisible)
        {
            var previewRoot = transform.Find("EditModePreview");
            if (previewRoot != null)
            {
                previewRoot.gameObject.SetActive(isVisible);
            }
        }

        private void EnsurePreviewActors()
        {
            var previewRoot = EnsureChild("EditModePreview");
            previewRoot.gameObject.SetActive(true);

            foreach (var actor in PreviewActors)
            {
                EnsureSprite(
                    actor.Name,
                    previewRoot,
                    actor.Position,
                    new Vector3(0.55f, 0.55f, 1f),
                    actor.Color,
                    30).gameObject.SetActive(true);
            }
        }

        private Transform EnsureChild(string childName)
        {
            var child = transform.Find(childName);
            if (child != null)
            {
                return child;
            }

            var childObject = new GameObject(childName);
            childObject.transform.SetParent(transform, false);
            return childObject.transform;
        }

        private static SpriteRenderer EnsureSprite(
            string name,
            Transform parent,
            Vector2 position,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            var child = parent.Find(name);
            GameObject gameObject;
            if (child == null)
            {
                gameObject = new GameObject(name);
                gameObject.transform.SetParent(parent, false);
            }
            else
            {
                gameObject = child.gameObject;
            }

            gameObject.transform.localPosition = new Vector3(position.x, position.y, 0f);
            gameObject.transform.localScale = scale;

            var renderer = gameObject.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = gameObject.AddComponent<SpriteRenderer>();
            }

            renderer.sprite = GetSolidSprite();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static Sprite GetSolidSprite()
        {
            if (solidSprite != null)
            {
                return solidSprite;
            }

            solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            return solidSprite;
        }

        private readonly struct PreviewActorState
        {
            public PreviewActorState(string name, Vector2 position, Color color)
            {
                Name = name;
                Position = position;
                Color = color;
            }

            public string Name { get; }

            public Vector2 Position { get; }

            public Color Color { get; }
        }
    }
}
