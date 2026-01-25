using System.IO;
using Peribind.Unity.ScriptableObjects;
using UnityEditor;
using UnityEngine;

namespace Peribind.Unity.EditorTools
{
    public class PieceIconGeneratorWindow : EditorWindow
    {
        [SerializeField] private PieceCatalogSO catalog;
        [SerializeField] private DefaultAsset outputFolder;
        [SerializeField] private int iconSize = 128;
        [SerializeField] private int padding = 8;
        [SerializeField] private float spritePixelsPerUnit = 100f;
        [SerializeField] private Color fillColor = Color.white;
        [SerializeField] private Color lineColor = new Color(0f, 0f, 0f, 0.4f);
        [SerializeField] private int lineThickness = 1;
        [SerializeField] private bool assignToPieces = true;

        [MenuItem("Peribind/Piece Icon Generator")]
        public static void ShowWindow()
        {
            GetWindow<PieceIconGeneratorWindow>("Piece Icon Generator");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Piece Icon Generator", EditorStyles.boldLabel);
            catalog = (PieceCatalogSO)EditorGUILayout.ObjectField("Catalog", catalog, typeof(PieceCatalogSO), false);
            outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
            iconSize = EditorGUILayout.IntField("Icon Size", iconSize);
            padding = EditorGUILayout.IntField("Padding", padding);
            spritePixelsPerUnit = EditorGUILayout.FloatField("Sprite PPU", spritePixelsPerUnit);
            fillColor = EditorGUILayout.ColorField("Fill Color", fillColor);
            lineColor = EditorGUILayout.ColorField("Line Color", lineColor);
            lineThickness = EditorGUILayout.IntField("Line Thickness", lineThickness);
            assignToPieces = EditorGUILayout.Toggle("Assign to Pieces", assignToPieces);

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(catalog == null))
            {
                if (GUILayout.Button("Generate Icons"))
                {
                    GenerateIcons();
                }
            }
        }

        private void GenerateIcons()
        {
            if (catalog == null)
            {
                Debug.LogError("Piece Icon Generator: Catalog is not assigned.");
                return;
            }

            var folderPath = GetOutputFolderPath();
            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.LogError("Piece Icon Generator: Output folder is invalid.");
                return;
            }

            Directory.CreateDirectory(folderPath);

            foreach (var piece in catalog.Pieces)
            {
                if (piece == null || piece.Cells.Count == 0)
                {
                    continue;
                }

                var texture = BuildTexture(piece, iconSize, padding, fillColor, lineColor, lineThickness);
                var bytes = texture.EncodeToPNG();
                DestroyImmediate(texture);

                var fileName = $"{piece.Id}_icon.png";
                var assetPath = Path.Combine(folderPath, fileName).Replace("\\", "/");
                File.WriteAllBytes(assetPath, bytes);

                AssetDatabase.ImportAsset(assetPath);
                ConfigureSpriteImporter(assetPath);

                if (assignToPieces)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    if (sprite != null)
                    {
                        var so = new SerializedObject(piece);
                        so.FindProperty("icon").objectReferenceValue = sprite;
                        so.ApplyModifiedProperties();
                    }
                }
            }

            AssetDatabase.Refresh();
        }

        private string GetOutputFolderPath()
        {
            if (outputFolder == null)
            {
                return "Assets/UI/PieceIcons";
            }

            var path = AssetDatabase.GetAssetPath(outputFolder);
            if (AssetDatabase.IsValidFolder(path))
            {
                return path;
            }

            return null;
        }

        private void ConfigureSpriteImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = spritePixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        private static Texture2D BuildTexture(PieceDefinitionSO piece, int size, int padding, Color color, Color line, int thickness)
        {
            var minX = piece.Cells[0].x;
            var minY = piece.Cells[0].y;
            var maxX = piece.Cells[0].x;
            var maxY = piece.Cells[0].y;

            for (var i = 1; i < piece.Cells.Count; i++)
            {
                var cell = piece.Cells[i];
                if (cell.x < minX) minX = cell.x;
                if (cell.y < minY) minY = cell.y;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.y > maxY) maxY = cell.y;
            }

            var widthCells = maxX - minX + 1;
            var heightCells = maxY - minY + 1;
            var available = Mathf.Max(1, size - padding * 2);
            var cellPixels = Mathf.Max(1, Mathf.FloorToInt(available / Mathf.Max(widthCells, heightCells)));

            var totalWidth = widthCells * cellPixels;
            var totalHeight = heightCells * cellPixels;
            var startX = (size - totalWidth) / 2;
            var startY = (size - totalHeight) / 2;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = clear;
            }
            texture.SetPixels(pixels);

            foreach (var cell in piece.Cells)
            {
                var localX = cell.x - minX;
                var localY = cell.y - minY;
                var px = startX + localX * cellPixels;
                var py = startY + localY * cellPixels;

                for (var x = 0; x < cellPixels; x++)
                {
                    for (var y = 0; y < cellPixels; y++)
                    {
                        texture.SetPixel(px + x, py + y, color);
                    }
                }
            }

            DrawGridLines(texture, widthCells, heightCells, cellPixels, startX, startY, line, thickness);

            texture.Apply();
            return texture;
        }

        private static void DrawGridLines(Texture2D texture, int widthCells, int heightCells, int cellPixels, int startX, int startY, Color line, int thickness)
        {
            if (thickness <= 0)
            {
                return;
            }

            var totalWidth = widthCells * cellPixels;
            var totalHeight = heightCells * cellPixels;

            for (var x = 1; x < widthCells; x++)
            {
                var px = startX + x * cellPixels;
                for (var t = 0; t < thickness; t++)
                {
                    var drawX = px + t;
                    for (var y = 0; y < totalHeight; y++)
                    {
                        texture.SetPixel(drawX, startY + y, line);
                    }
                }
            }

            for (var y = 1; y < heightCells; y++)
            {
                var py = startY + y * cellPixels;
                for (var t = 0; t < thickness; t++)
                {
                    var drawY = py + t;
                    for (var x = 0; x < totalWidth; x++)
                    {
                        texture.SetPixel(startX + x, drawY, line);
                    }
                }
            }
        }
    }
}
