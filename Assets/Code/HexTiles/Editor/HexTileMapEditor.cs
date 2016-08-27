﻿using UnityEngine;
using System.Collections;
using UnityEditor;
using System;
using RSG;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditor.AnimatedValues;
using System.Collections.Generic;
using RSG.Utils;

namespace HexTiles.Editor
{
    /// <summary>
    /// Editor for hex tile maps. Contains code for interacting with and painting tiles
    /// in the editor.
    /// </summary>
    [CustomEditor(typeof(HexTileMap))]
    public class HexTileMapEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Struct containg normal and selected icons for tool buttons.
        /// </summary>
        private struct ButtonIcon
        {
            public Texture2D NormalIcon;

            public Texture2D SelectedIcon;
        }

        private ButtonIcon[] toolIcons = {};

        /// <summary>
        /// States that the UI can be in.
        /// </summary>
        private static readonly string[] States = 
        {
            "Select",
            "Paint tiles",
            "Material paint", 
            "Erase",
            "Settings"
        };

        /// <summary>
        /// Root state for state machine.
        /// </summary>
        private IState rootState;

        /// <summary>
        /// Index of the currently selected tool.
        /// </summary>
        private int selectedToolIndex = 0;

        private static int hexTileEditorHash = "HexTileEditor".GetHashCode();

        /// <summary>
        /// The object we're editing.
        /// </summary>
        private HexTileMap hexMap;

        private IEnumerable<HexPosition> highlightedTiles = Enumerable.Empty<HexPosition>();
        private IEnumerable<HexPosition> nextTilePositions = Enumerable.Empty<HexPosition>();

        private AnimBool showTileCoordinateFormat;

        /// <summary>
        /// Center of the current selection.
        /// </summary>
        private HexCoords centerSelectedTileCoords;

        /// <summary>
        /// Current size of the area we want to effect by adding/removing/paining over tiles.
        /// </summary>
        int brushSize = 1;

        private void Initialise()
        {
            rootState = new StateMachineBuilder()
                .State("Select")
                    .Enter(evt => selectedToolIndex = 0)
                    .Update((state, dt) =>
                    {
                        ShowHelpBox("Select", "Pick a hex tile to manually edit its properties.");

                        if (hexMap.SelectedTile != null && hexMap.Tiles[hexMap.SelectedTile] != null)
                        {
                            var currentTile = hexMap.Tiles[hexMap.SelectedTile];

                            // Tile info
                            GUILayout.Label("Tile position", EditorStyles.boldLabel);

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label("Column", GUILayout.Width(EditorGUIUtility.labelWidth));
                            GUI.enabled = false;
                            EditorGUILayout.IntField(hexMap.SelectedTile.Q);
                            GUI.enabled = true;
                            EditorGUILayout.EndHorizontal();

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label("Row", GUILayout.Width(EditorGUIUtility.labelWidth));
                            GUI.enabled = false;
                            EditorGUILayout.IntField(hexMap.SelectedTile.R);
                            GUI.enabled = true;
                            EditorGUILayout.EndHorizontal();

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Label("Elevation", GUILayout.Width(EditorGUIUtility.labelWidth));
                            GUI.enabled = false;
                            EditorGUILayout.FloatField(currentTile.Elevation);
                            GUI.enabled = true;
                            EditorGUILayout.EndHorizontal();

                            // Tile settings
                            GUILayout.Label("Settings", EditorStyles.boldLabel);

                            var currentMaterial = currentTile.Material;
                            var newMaterial = (Material)EditorGUILayout.ObjectField("Material", currentMaterial, typeof(Material), false);
                            if (currentMaterial != newMaterial)
                            {
                                currentTile.Material = newMaterial;
                                MarkSceneDirty();
                            }
                        }
                    })
                    .Event<SceneClickedEventArgs>("SceneClicked", (state, eventArgs) =>
                    {
                        if (eventArgs.Button == 0)
                        {
                            var tile = TryFindTileForMousePosition(eventArgs.Position);
                            if (tile != null)
                            {
                                hexMap.SelectedTile = tile.Coordinates;
                            }
                        }
                    })
                .End()
                .State<PaintState>("Paint tiles")
                    .Enter(state => 
                    {
                        selectedToolIndex = 1;
                    })
                    .Update((state, dt) =>
                    {
                        ShowHelpBox("Paint tiles", "Click and drag to add hex tiles at the specified height.");

                        bool sceneNeedsRepaint = false;

                        var paintHeight = EditorGUILayout.FloatField("Paint height", state.PaintHeight);
                        if (paintHeight != state.PaintHeight)
                        {
                            state.PaintHeight = paintHeight;
                            highlightedTiles.Each(tile => tile.Elevation = paintHeight);

                            sceneNeedsRepaint = true;
                        }

                        var paintOffsetHeight = EditorGUILayout.FloatField("Height offset", state.PaintOffset);
                        if (paintOffsetHeight != state.PaintOffset)
                        {
                            state.PaintOffset = paintOffsetHeight;
                            nextTilePositions.Each(tile => tile.Elevation = paintHeight + paintOffsetHeight);

                            sceneNeedsRepaint = true;
                        }

                        var newBrushSize = EditorGUILayout.IntSlider("Brush size", brushSize, 1, 10);
                        if (newBrushSize != brushSize)
                        {
                            brushSize = newBrushSize;

                            sceneNeedsRepaint = true;
                        }

                        hexMap.CurrentMaterial = (Material)EditorGUILayout.ObjectField("Material", hexMap.CurrentMaterial, typeof(Material), false);

                        if (sceneNeedsRepaint)
                        {
                            if (centerSelectedTileCoords != null)
                            {
                                UpdateHighlighedTiles(centerSelectedTileCoords.CoordinateRange(brushSize - 1), state.PaintHeight, state.PaintOffset);
                            }
                            SceneView.RepaintAll();
                        }
                    })
                    .Event("MouseMove", state =>
                    {
                        var highlightedPosition = GetWorldPositionForMouse(Event.current.mousePosition, state.PaintHeight);
                        if (highlightedPosition != null)
                        {
                            centerSelectedTileCoords = hexMap.QuantizeVector3ToHexCoords(highlightedPosition.GetValueOrDefault());

                            UpdateHighlighedTiles(centerSelectedTileCoords.CoordinateRange(brushSize - 1), state.PaintHeight, state.PaintOffset);
                        }
                        Event.current.Use();
                    })
                    .Event<SceneClickedEventArgs>("SceneClicked", (state, eventArgs) =>
                    {
                        if (eventArgs.Button == 0)
                        {
                            var position = GetWorldPositionForMouse(eventArgs.Position, state.PaintHeight);
                            if (position != null)
                            {
                                // Select the tile that was clicked on.
                                centerSelectedTileCoords = hexMap.QuantizeVector3ToHexCoords(position.GetValueOrDefault());
                                var coords = centerSelectedTileCoords.CoordinateRange(brushSize - 1);

                                // Create tile
                                foreach (var hex in coords)
                                {
                                    var newTile = hexMap.CreateAndAddTile(
                                        new HexPosition(hex, state.PaintHeight + state.PaintOffset),
                                        hexMap.CurrentMaterial);

                                    EditorUtility.SetSelectedWireframeHidden(newTile.GetComponent<Renderer>(), true);
                                }

                                hexMap.SelectedTile = centerSelectedTileCoords;

                                MarkSceneDirty();
                            }
                        }
                    })
                    .Exit(state =>
                    {
                        highlightedTiles = Enumerable.Empty<HexPosition>();
                        nextTilePositions = Enumerable.Empty<HexPosition>();

                        hexMap.NextTilePositions = null;
                    })
                .End()
                .State("Material paint")
                    .Enter(state =>
                    {
                        selectedToolIndex = 2;
                    })
                    .Update((state, dt) =>
                    {
                        bool sceneNeedsRepaint = false;

                        ShowHelpBox("Material paint", "Paint over existing tiles to change their material.");

                        var newBrushSize = EditorGUILayout.IntSlider("Brush size", brushSize, 1, 10);
                        if (newBrushSize != brushSize)
                        {
                            brushSize = newBrushSize;

                            sceneNeedsRepaint = true;
                        }

                        hexMap.CurrentMaterial = (Material)EditorGUILayout.ObjectField("Material", hexMap.CurrentMaterial, typeof(Material), false);

                        EditorGUILayout.Space();

                        if (GUILayout.Button("Apply to all tiles"))
                        {
                            ApplyCurrentMaterialToAllTiles();
                            MarkSceneDirty();

                            sceneNeedsRepaint = true;
                        }

                        if (sceneNeedsRepaint)
                        {
                            SceneView.RepaintAll();
                        }
                    })
                    .Event("MouseMove", state =>
                    {
                        var centerTile = TryFindTileForMousePosition(Event.current.mousePosition);
                        if (centerTile != null)
                        {
                            highlightedTiles = centerTile.Coordinates.CoordinateRange(brushSize - 1)
                                .Where(tile => hexMap.Tiles.Contains(tile))
                                .Select(tile => new HexPosition(tile, hexMap.Tiles[tile].Elevation));
                        }
                        Event.current.Use();
                    })
                    .Event<SceneClickedEventArgs>("SceneClicked", (state, eventArgs) =>
                    {
                        if (eventArgs.Button == 0)
                        {
                            var tilePosition = TryFindTileForMousePosition(eventArgs.Position);
                            HexTile tile;
                            if (tilePosition != null && hexMap.Tiles.TryGetValue(tilePosition.Coordinates, out tile))
                            {
                                // Select that the tile that was clicked on.
                                hexMap.SelectedTile = tilePosition.Coordinates;

                                // Change the material on the tile
                                tile.Material = hexMap.CurrentMaterial;
                            }

                            Event.current.Use();
                        }
                    })
                .End()
                .State("Erase")
                    .Enter(evt => selectedToolIndex = 3)
                    .Update((state, dt) => 
                    {
                        ShowHelpBox("Erase", "Click and drag on existing hex tiles to remove them.");
                    })
                    .Event<SceneClickedEventArgs>("SceneClicked", (state, eventArgs) =>
                    {
                        if (eventArgs.Button == 0)
                        {
                            var tile = TryFindTileForMousePosition(eventArgs.Position);
                            if (tile != null)
                            {
                                // Select the tile that was clicked on.
                                hexMap.SelectedTile = tile.Coordinates;
                                // Destroy tile
                                if (hexMap.TryRemovingTile(tile.Coordinates))
                                {
                                    MarkSceneDirty();
                                }
                            }
                        }
                    })
                .End()
                .State<SettingsState>("Settings")
                    .Enter(state => 
                    {
                        selectedToolIndex = 4;
                        state.HexSize = hexMap.hexWidth;

                        showTileCoordinateFormat.value = hexMap.DrawHexPositionHandles;
                    })
                    .Update((state, dt) =>
                    {
                        ShowHelpBox("Settings", "Configure options for the whole tile map.");

                        var shouldDrawPositionHandles = EditorGUILayout.Toggle("Show tile positions", hexMap.DrawHexPositionHandles);
                        if (shouldDrawPositionHandles != hexMap.DrawHexPositionHandles)
                        {
                            hexMap.DrawHexPositionHandles = shouldDrawPositionHandles;
                            SceneView.RepaintAll();
                            MarkSceneDirty();

                            showTileCoordinateFormat.target = shouldDrawPositionHandles;
                        }

                        if (EditorGUILayout.BeginFadeGroup(showTileCoordinateFormat.faded))
                        {
                            var newHandleFormat = (HexTileMap.HexCoordinateFormat)
                                EditorGUILayout.EnumPopup("Tile coordinate format", hexMap.HexPositionHandleFormat);
                            if (newHandleFormat != hexMap.HexPositionHandleFormat)
                            {
                                hexMap.HexPositionHandleFormat = newHandleFormat;
                                SceneView.RepaintAll();
                            }
                        }
                        EditorGUILayout.EndFadeGroup();

                        state.HexSize = EditorGUILayout.FloatField("Tile size", state.HexSize);
                        if (state.HexSize != hexMap.hexWidth)
                        {
                            state.Dirty = true;
                        }

                        if (GUILayout.Button("Re-generate all tile geometry"))
                        {
                            hexMap.RegenerateAllTiles();
                            MarkSceneDirty();
                        }

                        if (GUILayout.Button("Clear all tiles"))
                        {
                            if (EditorUtility.DisplayDialog("Clear all tiles", 
                                "Are you sure you want to delete all tiles in this hex tile map?", 
                                "Clear", 
                                "Cancel"))
                            {
                                hexMap.ClearAllTiles();
                                MarkSceneDirty();
                            }
                        }

                        EditorGUILayout.Space();

                        GUI.enabled = state.Dirty;
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Apply", GUILayout.Width(160)))
                        {
                            Debug.Log("Saving settings");
                            hexMap.hexWidth = state.HexSize;
                            hexMap.RegenerateAllTiles();
                            MarkSceneDirty();

                            state.Dirty = false;
                        }
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                        GUI.enabled = true;
                    })
                .End()
                .Build();
            
            rootState.ChangeState("Select");

            if (EditorGUIUtility.isProSkin)
            {
                toolIcons = new ButtonIcon[] {
                    new ButtonIcon{ NormalIcon = LoadImage("mouse-pointer_44_pro"), SelectedIcon = LoadImage("mouse-pointer_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("add-hex_44_pro"), SelectedIcon = LoadImage("add-hex_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("paint-brush_44_pro"), SelectedIcon = LoadImage("paint-brush_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("eraser_44_pro"), SelectedIcon = LoadImage("eraser_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("cog_44_pro"), SelectedIcon = LoadImage("cog_44_selected") },
                };
            }
            else
            {
                toolIcons = new ButtonIcon[] {
                    new ButtonIcon{ NormalIcon = LoadImage("mouse-pointer_44"), SelectedIcon = LoadImage("mouse-pointer_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("add-hex_44"), SelectedIcon = LoadImage("add-hex_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("paint-brush_44"), SelectedIcon = LoadImage("paint-brush_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("eraser_44"), SelectedIcon = LoadImage("eraser_44_selected") },
                    new ButtonIcon{ NormalIcon = LoadImage("cog_44"), SelectedIcon = LoadImage("cog_44_selected") },
                };
            }
        }

        private void UpdateHighlighedTiles(IEnumerable<HexCoords> coords, float paintHeight, float paintOffset)
        {
            highlightedTiles = coords.Select(tile => new HexPosition(tile, paintHeight));
            nextTilePositions = coords.Select(tile => new HexPosition(tile, paintHeight + paintOffset));
        }

        /// <summary>
        /// Applies the material stored in hexMap.CurrentMaterial to all the tiles in hexMap
        /// </summary>
        private void ApplyCurrentMaterialToAllTiles()
        {
            foreach (var tile in hexMap.Tiles)
            {
                tile.Material = hexMap.CurrentMaterial;
            }
        }

        /// <summary>
        /// Tell Unity that a change has been made and we have to save the scene.
        /// </summary>
        private void MarkSceneDirty()
        {
#if UNITY_5_3_OR_NEWER
            // TODO: Undo.RecordObject also marks the scene dirty, so this will no longer be necessary once undo support is added.
            EditorSceneManager.MarkSceneDirty(hexMap.gameObject.scene);
#else
            EditorUtility.SetDirty(hexMap.gameObject);
#endif
        }

        void OnSceneGUI()
        {
            if (hexMap.DrawHexPositionHandles)
            {
                DrawHexPositionHandles();
            }

            hexMap.HighlightedTiles = highlightedTiles;
            hexMap.NextTilePositions = nextTilePositions;


            // Handle mouse input
            var controlId = GUIUtility.GetControlID(hexTileEditorHash, FocusType.Passive);
            switch (Event.current.GetTypeForControl(controlId))
            {
                case EventType.MouseMove:

                    rootState.TriggerEvent("MouseMove");
                    
                    break;
                case EventType.MouseDrag:
                case EventType.MouseDown:

                    rootState.TriggerEvent("MouseMove");

                    // Don't do anything if the user alt-left clicks to rotate the camera.
                    if (Event.current.button == 0 && Event.current.alt)
                    {
                        break;
                    }

                    var eventArgs = new SceneClickedEventArgs { 
                        Button = Event.current.button, 
                        Position = Event.current.mousePosition
                    };
                    rootState.TriggerEvent("SceneClicked", eventArgs);

                    // Disable the normal interaction with objects in the scene so that we 
                    // can do things with tiles.
                    if (Event.current.button == 0)
                    {
                        Repaint();
                        Event.current.Use();
                    }
                    break;
                case EventType.layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }

        /// <summary>
        /// Draw handles with the position of each hex tile above that tile in the scene.
        /// </summary>
        private void DrawHexPositionHandles()
        {
            foreach (var tile in hexMap.Tiles)
            {
                var position = tile.transform.position;

                // Only draw this handle if the tile is in front of the camera.
                var cameraTransform = SceneView.currentDrawingSceneView.camera.transform;
                var cameraToTile = cameraTransform.position - position;
                if (Vector3.Dot(cameraToTile, cameraTransform.forward) > 0)
                {
                    continue;
                }

                var hexCoords = hexMap.QuantizeVector3ToHexCoords(position);
                var labelText = string.Empty;
                switch (hexMap.HexPositionHandleFormat)
                {
                    case HexTileMap.HexCoordinateFormat.Axial:
                        labelText = hexCoords.ToString();
                        break;
                    case HexTileMap.HexCoordinateFormat.OffsetOddQ:
                        labelText = hexCoords.ToOffset().ToString();
                        break;
                }
                Handles.Label(position, labelText);
            }
        }

        void OnEnable()
        {
            hexMap = (HexTileMap)target;

            // Init anim bools
            showTileCoordinateFormat = new AnimBool(Repaint);

            foreach (var renderer in hexMap.GetComponentsInChildren<Renderer>())
            {
                EditorUtility.SetSelectedWireframeHidden(renderer, true);
            }

            Initialise();
        }

        public override void OnInspectorGUI()
        {
            var toolbarContent = new GUIContent[]
            {
                new GUIContent(GetToolButtonIcon(0), "Select"),
                new GUIContent(GetToolButtonIcon(1), "Paint tiles"),
                new GUIContent(GetToolButtonIcon(2), "Material paint"),
                new GUIContent(GetToolButtonIcon(3), "Delete"),
                new GUIContent(GetToolButtonIcon(4), "Settings")
            };

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var newSelectedTool = GUILayout.Toolbar(selectedToolIndex, toolbarContent, "command");
            if (newSelectedTool != selectedToolIndex)
            {
                rootState.ChangeState(States[newSelectedTool]);
                SceneView.RepaintAll();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            rootState.Update(Time.deltaTime);
        }

        /// <summary>
        /// Helper function to get the correct icon for a tool button.
        /// </summary>
        private Texture2D GetToolButtonIcon(int index)
        {
            return selectedToolIndex == index ? toolIcons[index].SelectedIcon : toolIcons[index].NormalIcon;
        }

        Texture2D LoadImage(string resource)
        {
            var image = Resources.Load<Texture2D>(resource);
            if (image == null)
            {
                throw new ApplicationException("Failed to load image from resource \"" + resource + "\"");
            }

            return image;
        }

        /// <summary>
        /// Show a UI with some information about the selected tool.
        /// </summary>
        private void ShowHelpBox(string toolName, string description)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(toolName);
            GUILayout.Label(description, EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Return the point we would hit at the specified height for the specified mouse position.
        /// </summary>
        private Nullable<Vector3> GetWorldPositionForMouse(Vector2 mousePosition, float placementHeight)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0, placementHeight, 0));

            var distance = 0f;
            if (plane.Raycast(ray, out distance))
            {
                return ray.GetPoint(distance);
            }

            return null;
        }

        /// <summary>
        /// Try to find a tile by raycasting from the specified mouse position. 
        /// Returns null if no tile was found.
        /// </summary>
        private HexPosition TryFindTileForMousePosition(Vector2 mousePosition)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            return Physics.RaycastAll(ray, 1000f)
                .Where(hit => hit.collider.GetComponent<HexTile>() != null)
                .OrderBy(hit => hit.distance)
                .Select(hit => new HexPosition(hexMap.QuantizeVector3ToHexCoords(hit.point), hit.point.y))
                .FirstOrDefault();
        }

        /// <summary>
        /// State for when we're painting tiles.
        /// </summary>
        private class PaintState : AbstractState
        {
            public float PaintHeight;

            public float PaintOffset;
        }

        /// <summary>
        /// State for when we're in the map settings mode.
        /// </summary>
        private class SettingsState : AbstractState
        {
            /// <summary>
            /// Whether or not a value has been changed and needs to be saved.
            /// </summary>
            public bool Dirty = false;

            public float HexSize;
        }

        /// <summary>
        /// Event args for when the user clicks in the scene. Passed on to whatever
        /// the active tool is.
        /// </summary>
        private class SceneClickedEventArgs : EventArgs
        {
            public int Button;

            public Vector2 Position;
        }
    }
}
