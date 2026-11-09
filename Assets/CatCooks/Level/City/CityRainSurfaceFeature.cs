using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CatCooks.Level
{
    /// <summary>
    /// МОКРЫЕ ПОВЕРХНОСТИ — проход URP (Render Graph, Unity 6) после непрозрачных, до прозрачных (капли и всплески
    /// рисуются поверх). Шейдер Hidden/CatCooks/City/Rain Surface: тёмные капли → мокрый пол → лужицы → залито.
    /// Если на City Rain включено Wet Only Marked — сначала рисуется маска отмеченных объектов (City Wet Surface,
    /// бит слоя рендера 12), мокнут только они.
    /// Добавляется в URP Renderer меню «Дождь: поставить в сцену» (или руками: Add Renderer Feature → City Rain Surface).
    /// Пока сухо (_CC_WetOn = 0) и не зима — проход не ставится вовсе, ничего не стоит.
    /// </summary>
    public class CityRainSurfaceFeature : ScriptableRendererFeature
    {
        public Shader Shader;
        public Shader MaskShader;
        public RenderPassEvent Event = RenderPassEvent.BeforeRenderingTransparents;

        private Material _mat, _maskMat;
        private SurfacePass _pass;
        private static readonly int IdWetOn = UnityEngine.Shader.PropertyToID("_CC_WetOn");
        private static readonly int IdWinterOn = UnityEngine.Shader.PropertyToID("_CC_WinterOn");
        private static readonly int IdHailOn = UnityEngine.Shader.PropertyToID("_CC_HailOn");
        private static readonly int IdHeatOn = UnityEngine.Shader.PropertyToID("_CC_HeatOn");
        private static readonly int IdFogOn = UnityEngine.Shader.PropertyToID("_CC_FogOn");

        public override void Create()
        {
            if (Shader == null) Shader = UnityEngine.Shader.Find("Hidden/CatCooks/City/Rain Surface");
            if (MaskShader == null) MaskShader = UnityEngine.Shader.Find("Hidden/CatCooks/City/Wet Mask");
            if (Shader != null && _mat == null) _mat = CoreUtils.CreateEngineMaterial(Shader);
            if (MaskShader != null && _maskMat == null) _maskMat = CoreUtils.CreateEngineMaterial(MaskShader);
            _pass = new SurfacePass(_mat, _maskMat);
            _pass.renderPassEvent = Event;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_mat == null || _pass == null) return;
            CameraType ct = renderingData.cameraData.cameraType;
            if (ct != CameraType.Game && ct != CameraType.SceneView) return;
            if (renderingData.cameraData.camera != null && renderingData.cameraData.camera.name == "CC_FacadeCam") return;   // съёмка фасадов для фейк-этажей — без погоды
            if (UnityEngine.Shader.GetGlobalFloat(IdWetOn) < 0.5f && UnityEngine.Shader.GetGlobalFloat(IdWinterOn) < 0.5f
                && UnityEngine.Shader.GetGlobalFloat(IdHailOn) < 0.5f && UnityEngine.Shader.GetGlobalFloat(IdHeatOn) < 0.5f
                && UnityEngine.Shader.GetGlobalFloat(IdFogOn) < 0.5f) return;   // сухо, не зима, нет града, жары и тумана
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_mat);
            CoreUtils.Destroy(_maskMat);
            _mat = null;
            _maskMat = null;
        }

        private class SurfacePass : ScriptableRenderPass
        {
            private readonly Material _m, _mask;
            private readonly List<ShaderTagId> _tags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalGBuffer")
            };
            private static readonly int IdMask = UnityEngine.Shader.PropertyToID("_CC_WetMask");
            private static readonly int IdMaskOn = UnityEngine.Shader.PropertyToID("_CC_WetMaskOn");
            private static readonly int IdMaskUse = UnityEngine.Shader.PropertyToID("_CC_WetMaskUse");

            private class PassData
            {
                public TextureHandle Src;
                public Material Mat;
                public float MaskUse;
            }

            private class MaskData
            {
                public RendererListHandle List;
            }

            public SurfacePass(Material m, Material mask)
            {
                _m = m;
                _mask = mask;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_m == null) return;
                UniversalResourceData res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                TextureHandle src = res.activeColorTexture;

                // 1. маска «что мокнет» (только отмеченные City Wet Surface)
                bool useMask = UnityEngine.Shader.GetGlobalFloat(IdMaskOn) > 0.5f;
                TextureHandle mask = TextureHandle.nullHandle;
                if (useMask)
                {
                    if (_mask == null || CityWetSurface.All.Count == 0) return;     // отмечено ничего — мокнуть нечему
                    UniversalRenderingData rd = frameData.Get<UniversalRenderingData>();
                    UniversalCameraData cd = frameData.Get<UniversalCameraData>();
                    UniversalLightData ld = frameData.Get<UniversalLightData>();
                    DrawingSettings ds = RenderingUtils.CreateDrawingSettings(_tags, rd, cd, ld, SortingCriteria.CommonOpaque);
                    ds.overrideMaterial = _mask;
                    ds.overrideMaterialPassIndex = 0;
                    FilteringSettings fs = new FilteringSettings(RenderQueueRange.opaque, -1, CityWetSurface.LayerMask);
                    RendererListParams rp = new RendererListParams(rd.cullResults, ds, fs);
                    RendererListHandle rl = renderGraph.CreateRendererList(rp);

                    TextureDesc md = renderGraph.GetTextureDesc(src);
                    md.name = "CC_WetMask";
                    md.colorFormat = GraphicsFormat.R8_UNorm;
                    md.depthBufferBits = DepthBits.None;
                    md.clearBuffer = true;
                    md.clearColor = Color.clear;
                    mask = renderGraph.CreateTexture(md);

                    MaskData mdat;
                    using (IRasterRenderGraphBuilder b = renderGraph.AddRasterRenderPass<MaskData>("City Wet Mask", out mdat))
                    {
                        mdat.List = rl;
                        b.UseRendererList(rl);
                        b.SetRenderAttachment(mask, 0, AccessFlags.Write);
                        b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.Read);
                        b.SetGlobalTextureAfterPass(mask, IdMask);
                        b.SetRenderFunc((MaskData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.List));
                    }
                }

                // 2. мокрость поверх кадра
                TextureDesc desc = renderGraph.GetTextureDesc(src);
                desc.name = "CC_RainSurface";
                desc.clearBuffer = false;
                desc.depthBufferBits = DepthBits.None;
                TextureHandle dst = renderGraph.CreateTexture(desc);

                PassData data;
                using (IRasterRenderGraphBuilder b = renderGraph.AddRasterRenderPass<PassData>("City Rain Surface", out data))
                {
                    data.Src = src;
                    data.Mat = _m;
                    data.MaskUse = useMask ? 1f : 0f;
                    b.UseTexture(src, AccessFlags.Read);
                    if (res.cameraDepthTexture.IsValid()) b.UseTexture(res.cameraDepthTexture, AccessFlags.Read);
                    if (useMask) b.UseTexture(mask, AccessFlags.Read);
                    b.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    b.AllowGlobalStateModification(true);
                    b.UseAllGlobalTextures(true);                                  // карта теней солнца (тени на снегу и льду), текстуры льда
                    b.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalFloat(IdMaskUse, d.MaskUse);
                        Blitter.BlitTexture(ctx.cmd, d.Src, new Vector4(1f, 1f, 0f, 0f), d.Mat, 0);
                    });
                }
                res.cameraColor = dst;
            }
        }
    }
}
