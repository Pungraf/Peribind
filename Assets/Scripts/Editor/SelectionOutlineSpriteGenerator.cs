using System.IO;
using UnityEditor;
using UnityEngine;

namespace Peribind.Unity.EditorTools
{
    public static class SelectionOutlineSpriteGenerator
    {
        [MenuItem("Peribind/Generate Selection Outline Sprite")]
        public static void Generate()
        {
            const int size = 64;
            const int border = 3;
            var folder = "Assets/UI/SelectionOutline";
            Directory.CreateDirectory(folder);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = clear;
            }
            texture.SetPixels(pixels);

            var borderColor = new Color(1f, 1f, 1f, 1f);
            for (var x = 0; x < size; x++)
            {
                for (var y = 0; y < size; y++)
                {
                    var onLeft = x < border;
                    var onRight = x >= size - border;
                    var onBottom = y < border;
                    var onTop = y >= size - border;
                    if (onLeft || onRight || onBottom || onTop)
                    {
                        texture.SetPixel(x, y, borderColor);
                    }
                }
            }

            texture.Apply();

            var path = $"{folder}/SelectionOutline.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            Debug.Log("Selection outline sprite generated at Assets/UI/SelectionOutline/SelectionOutline.png");
        }
    }
}
