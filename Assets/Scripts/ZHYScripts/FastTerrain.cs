using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using UnityEngine;

public class FastTerrain : MonoBehaviour // MonoBehaviour 是一个基类，所有 Unity 脚本都派生自该类
{
    public Texture2D albedoAtlas;
    public Texture2D normalAtlas;

    //splat 区分id和Weight 主要是因为 id不能插值 但weight需要插值 如果分辨率精度够大 point 采样够平滑 就不需要分2张
    public Texture2D splatID;
    public Texture2D splatWeight;
    public Shader terrainShader;
    public TerrainData normalTerrainData;
    public TerrainData emptyTerrainData;
    [ContextMenu("MakeAlbedoAtlas")]
    // Update is called once per frame
    void MakeAlbedoAtlas()
    {

        int sqrCount = 4;
        int wid = normalTerrainData.splatPrototypes[0].texture.width;
        int hei = normalTerrainData.splatPrototypes[0].texture.height;


        albedoAtlas = new Texture2D(sqrCount * wid, sqrCount * hei, TextureFormat.RGBA32, true);
        normalAtlas = new Texture2D(sqrCount * wid, sqrCount * hei, TextureFormat.RGBA32, true);

        for (int i = 0; i < sqrCount; i++)
        {
            for (int j = 0; j < sqrCount; j++)
            {
                int index = i * sqrCount + j;

                if (index >= normalTerrainData.splatPrototypes.Length) break;
                albedoAtlas.SetPixels(j * (wid), i * (hei), wid, hei, // SetPixels()设置像素颜色块：接收一个颜色数组，然后更改纹理的整个 Mip 级别的像素颜色
                    normalTerrainData.splatPrototypes[index].texture.GetPixels()); // GetPixels(): Returns in pixels[] a copy of the data in the bitmap. Each value is a packed int representing a Color.
                // normalAtlas.SetPixels(j * (wid), i * (hei), wid, hei,
                //    normalTerrainData.splatPrototypes[index].normalMap.GetPixels());
            }
        }

        albedoAtlas.Apply(); // 调用 Apply 可实际将更改的像素上传到显卡
        normalAtlas.Apply();
        File.WriteAllBytes(Application.dataPath+"/albedoAtlas_2.png",albedoAtlas.EncodeToPNG());
        File.WriteAllBytes(Application.dataPath + "/normalAtlas_2.png", normalAtlas.EncodeToPNG());
        DestroyImmediate(albedoAtlas);
        DestroyImmediate(normalAtlas);
    }


    struct SplatData
    {
        public int id;
        public float weight;
        public float nearWeight;
    }


    [ContextMenu("MakeSplat")]
    // Update is called once per frame
    void MakeSplat()
    {
        // Debug.Log(normalTerrainData.alphamapTextures[0] == null);
        int wid = normalTerrainData.alphamapTextures[0].width;
        int hei = normalTerrainData.alphamapTextures[0].height;
        List<Color[]> colors = new List<Color[]>();
        // t.terrainData.alphamapTextures[i].GetPixels();


        for (int i = 0; i < normalTerrainData.alphamapTextures.Length; i++)
        {
            colors.Add(normalTerrainData.alphamapTextures[i].GetPixels()); // 把所有alphamapTextures放到colors列表里
        }

        splatID = new Texture2D(wid, hei, TextureFormat.RGB24, false, true);

        splatID.filterMode = FilterMode.Point;

        var splatIDColors = splatID.GetPixels();

        // 改用图片文件时可设置压缩为R8 代码生成有格式限制 空间有点浪费
        splatWeight = new Texture2D(wid, hei, TextureFormat.RGB24, false, true);
        splatWeight.filterMode = FilterMode.Bilinear;
        var splatWeightColors = splatWeight.GetPixels();

        for (int i = 0; i < hei; i++)
        {
            for (int j = 0; j < wid; j++)
            {
                List<SplatData> splatDatas = new List<SplatData>();
                int index = i * wid + j;

                //struct 是值引用 所以 Add到list后  可以复用（修改他属性不会影响已经加入的数据）
                for (int k = 0; k < colors.Count; k++)
                {
                    SplatData sd;
                    sd.id = k * 4;
                    sd.weight = colors[k][index].r;
                    sd.nearWeight = getNearWeight(colors[k], index, wid, 0);
                    splatDatas.Add(sd);
                    sd.id++;
                    sd.weight = colors[k][index].g;
                    sd.nearWeight = getNearWeight(colors[k], index, wid, 1);

                    splatDatas.Add(sd);
                    sd.id++;
                    sd.weight = colors[k][index].b;
                    sd.nearWeight = getNearWeight(colors[k], index, wid, 2);

                    splatDatas.Add(sd);
                    sd.id++;
                    sd.weight = colors[k][index].a;
                    sd.nearWeight = getNearWeight(colors[k], index, wid, 3);

                    splatDatas.Add(sd);
                }


                //按权重排序选出最重要几个
                splatDatas.Sort((x, y) => -(x.weight + x.nearWeight / 2).CompareTo(y.weight + y.nearWeight / 2)); // x.weight + x.nearWeight / 2 ???

                // 把最大权重的splatmap通道id值给了splatIDColors的r通道
                splatIDColors[index].r = splatDatas[0].id / 16f; // 为什么 / 16f？splatIDColors[index].g呢？
                
                int swapID = 0;
                if (j > 0) // 为什么要j > 0？
                {
                    if (Mathf.Abs(splatIDColors[index].r - splatIDColors[index - 1].g) < 0.5 / 16 ||
                        Mathf.Abs(splatIDColors[index].g - splatIDColors[index - 1].r) < 0.5 / 16) // 为什么判断条件是 < 0.5 / 16 ？float精度
                    {
                        //Debug.Log(splatIDColors[index].r + "/" + splatIDColors[index - 1].g);
                        swapID = 1;
                    }
                }

                if (i > 0)
                {
                    if (Mathf.Abs(splatIDColors[index].r - splatIDColors[index - wid].g) < 0.5 / 16 ||
                        Mathf.Abs(splatIDColors[index].g - splatIDColors[index - wid].r) < 0.5 / 16)
                    {
                        //Debug.Log(splatIDColors[index].r + "/" + splatIDColors[index - 1].g);
                        swapID = 1;
                    }
                }

                  


                //只存最重要2个图层 用一点压缩方案可以一张图存更多图层 ,这里最多支持16张
                splatIDColors[index].r = splatDatas[swapID].id / 16f;
                splatIDColors[index].g = splatDatas[1 - swapID].id / 16f;
                splatIDColors[index].b = 0;

                splatWeightColors[index].r =
                    splatDatas[swapID].weight +
                    (1 - splatDatas[0].weight - splatDatas[1].weight) / 2; //2张以后丢弃的权重平均加到这2张

                splatWeightColors[index].g = splatWeightColors[index].b = 0; // 权重图只有一个通道
            }
        }

        splatID.SetPixels(splatIDColors);
        splatID.Apply();


        splatWeight.SetPixels(splatWeightColors);
        splatWeight.Apply();
    }

    // 为什么要getNearWeight？过程？
    private float getNearWeight(Color[] colors, int index, int wid, int rgba)
    {
        float value = 0;
        for (int i = 1; i <= 3; i++)
        {
            value += colors[(index + colors.Length - i) % colors.Length][rgba];
            value += colors[(index + colors.Length + i) % colors.Length][rgba];
            value += colors[(index + colors.Length - wid * i) % colors.Length][rgba];
            value += colors[(index + colors.Length + wid * i) % colors.Length][rgba];
            value += colors[(index + colors.Length + (-1 - wid) * i) % colors.Length][rgba];
            value += colors[(index + colors.Length + (-1 + wid) * i) % colors.Length][rgba];
            value += colors[(index + colors.Length + (1 - wid) * i) % colors.Length][rgba];
            value += colors[(index + colors.Length + (1 + wid) * i) % colors.Length][rgba];
        }

        return value / (8 * 3);
    }


  
    [ContextMenu("UseFastMode")]
    void useFastMode()
    {
        Terrain t = GetComponent<Terrain>();
        t.terrainData = emptyTerrainData;
       
        // t.materialType = Terrain.MaterialType.Custom;
        if (t.materialTemplate == null)
        {
            t.materialTemplate = new Material(terrainShader); // FastMode和BuildinMode的区别就是这个shader？
        }
        else
        {
            t.materialTemplate.shader = terrainShader;
        }

        Shader.SetGlobalTexture("SpaltIDTex", splatID);
        Shader.SetGlobalTexture("SpaltWeightTex", splatWeight);
        Shader.SetGlobalTexture("AlbedoAtlas", albedoAtlas);
        Shader.SetGlobalTexture("NormalAtlas", normalAtlas);
    }

    [ContextMenu("UseBuildinMode")]
    void useBuildinMode()
    {
        Terrain t = GetComponent<Terrain>();
        t.terrainData = normalTerrainData;
        // t.materialType = Terrain.MaterialType.BuiltInStandard;
        t.materialTemplate = null;
    }


    private bool fastMode = false;

// 自选渲染模式
    private void OnGUI()
    {
        if (GUILayout.Button(fastMode ? "自定义渲染ing" : "引擎默认渲染ing"))
        {
            fastMode = !fastMode;
            if (fastMode)
            {
                useFastMode();
                Debug.Log("fast mode");
            }
            else
            {
                useBuildinMode();
                Debug.Log("buildin mode");
            }
        }
    }
}