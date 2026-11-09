using Godot;

namespace NewKOF;

/// <summary>Godot runtime checks using the original micro fixture, not third-party art.</summary>
public partial class MugenVisualSmoke : Node2D
{
    private MugenSpriteVisual? _gpuVisual;
    private int _gpuFrame;
    public override void _Ready()
    {
        try
        {
            var root = new Node2D(); AddChild(root);
            var visual = MugenSpriteVisual.TryLoad(root, "fixture", true, "res://tests/fixtures/mugen/")
                ?? throw new Exception("fixture binding failed");
            var sprite = root.GetChild<Sprite2D>(0);
            Require(visual.Apply("idle", 0, 0, false, false), "texture load");
            Require(sprite.Visible && sprite.Texture is not null, "sprite visible");
            Require(sprite.Position == new Vector2(1, -4) && sprite.Scale == Vector2.One, "axis offset");
            Require(visual.Apply("idle", 2, 2, false, false) && !sprite.Visible, "blank frame");
            Require(!visual.Apply("idle", 3, 3, false, false), "missing sprite fallback");
            Require(visual.Apply("idle", 4, 4, false, true), "held HV frame");
            Require(sprite.Position == new Vector2(-3, 2) && sprite.Scale == new Vector2(1, -1), "HV and facing");
            Require(!visual.Apply("missing", 0, 0, false, false), "missing action fallback");
            Require(!visual.Apply("missing_atlas", 0, 0, false, false) && !sprite.Visible, "missing atlas fallback");
            Require(MugenSpriteVisual.TryLoad(root, "absent", false, "res://tests/fixtures/mugen/") is null, "absent visual fallback");
            Require(visual.Apply("hold", 20, 20, false, false) && sprite.Scale == new Vector2(1, -1), "non-loop hold");
            visual.SetFlash(true);
            Require((bool)((ShaderMaterial)sprite.Material).GetShaderParameter("flash"), "flash on");
            visual.SetFlash(false);
            Require(!(bool)((ShaderMaterial)sprite.Material).GetShaderParameter("flash"), "flash reset");
            visual.Dispose();
            Require(sprite.IsQueuedForDeletion() && sprite.Texture is null, "release old sprite");
            int attempts = 0;
            var failed = MugenSpriteVisual.TryLoad(root, "fixture", true, "res://tests/fixtures/mugen/", _ => { attempts++; return null; })
                ?? throw new Exception("fixture rebind failed");
            Require(!failed.Apply("idle", 0, 0, false, false) && !failed.Apply("idle", 0, 0, false, false), "loader failure fallback");
            Require(attempts == 1, "failed texture is cached");
            failed.Dispose();
            var throwing = MugenSpriteVisual.TryLoad(root, "fixture", true, "res://tests/fixtures/mugen/", _ => throw new System.IO.IOException("fixture load failure"))
                ?? throw new Exception("throwing loader setup failed");
            Require(!throwing.Apply("idle", 0, 0, false, false), "loader exception fallback");
            throwing.Dispose();
            if (OS.GetCmdlineUserArgs().Contains("--gpu-pixels"))
            {
                RenderingServer.SetDefaultClearColor(Colors.Black);
                var gpuRoot = new Node2D { Position = new Vector2(100, 100), Scale = new Vector2(16, 16) };
                AddChild(gpuRoot);
                _gpuVisual = MugenSpriteVisual.TryLoad(gpuRoot, "fixture", true, "res://tests/fixtures/mugen/")
                    ?? throw new Exception("GPU fixture load failed");
                Require(_gpuVisual.Apply("idle", 0, 0, false, false), "GPU fixture apply");
                return;
            }
            GD.Print("MUGEN_SMOKE_PASS");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("MUGEN_SMOKE_FAIL: " + ex);
            GetTree().Quit(1);
        }
    }
    public override void _Process(double delta)
    {
        if (_gpuVisual is null) return;
        try
        {
            _gpuFrame++;
            if (_gpuFrame is not (3 or 6 or 9)) return;
            var image = GetViewport().GetTexture().GetImage();
            Color visible = image.GetPixel(136, 56), transparent = image.GetPixel(120, 40);
            Require(transparent.R < 0.05f && transparent.G < 0.05f && transparent.B < 0.05f, "transparent pixel preserved");
            if (_gpuFrame == 6)
                Require(visible.R > 0.9f && visible.G > 0.9f && visible.B > 0.9f, "visible pixel is white");
            else
                Require(visible.R > 0.7f && visible.G < 0.3f && visible.B < 0.3f, "palette color restored");
            if (_gpuFrame == 3) _gpuVisual.SetFlash(true);
            else if (_gpuFrame == 6) _gpuVisual.SetFlash(false);
            else { GD.Print("MUGEN_GPU_PIXELS_PASS"); GetTree().Quit(0); }
        }
        catch (Exception ex)
        {
            GD.PushError("MUGEN_GPU_PIXELS_FAIL: " + ex);
            GetTree().Quit(1);
        }
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
