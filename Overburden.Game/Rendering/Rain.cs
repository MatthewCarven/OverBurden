using Godot;

namespace Overburden.Game.Rendering;

/// <summary>
/// The rain: a camera-locked box of GPU particles that fall, are killed by the ground, and splash
/// where they land.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything about a drop happens on the GPU.</b> Position, respawn and the occlusion kill are
/// all in <c>Rain.gdshader</c>; what this class does is move the box, push four
/// numbers a frame, and own the RIDs. That is DESIGN's "one draw call" claim, and it is why the
/// rain can be dense without <c>update ms</c> knowing about it.
/// </para>
/// <para>
/// <b>The box is camera-locked and the particles are not.</b> <c>LocalCoords</c> is false, so a
/// drop is emitted in world space and falls through it while the emitter walks away — which is the
/// difference between rain and a shower attached to the player's head. The box only decides where
/// new drops appear and where old ones are recycled.
/// </para>
/// <para>
/// <b>There are no splash particles, and that is a reported gap rather than an oversight.</b>
/// M7c-2 asks for splashes at the kill point; the system was built twice — once through
/// <c>emit_subparticle</c> from the rain's process shader, once as a second emitter placing
/// droplets on the same sky-height field — and neither drew anything that survived scrutiny. What
/// was established, by counting pixels in forced-colour screenshots rather than by eye: the splash
/// draw pass and process shader work (85 pixels self-emitting), the sub-emitter link suppresses
/// self-emission and delivers nothing in its place (0 pixels), re-parenting it as a child gets
/// particles out but all at one point, and a <c>texture()</c> call inside a particle
/// <c>start()</c> silently yields particles that never appear. The last of those is worth the most
/// and is why the field lookup would have to live in <c>process()</c>. Cut rather than shipped
/// dead, because a particle system that draws nothing is indistinguishable from one that is
/// working and subtle. <b>See the worklog; this wants its own pass.</b>
/// </para>
/// <para>
/// <b>Amount is a buffer size, not a rain rate.</b> It cannot change without reallocating, so the
/// storm's intensity gates each drop's <c>ACTIVE</c> inside the shader instead: at intensity 0.3
/// roughly three drops in ten are alive and the rest cost nothing. That is what lets one system
/// cover drizzle and downpour, and it is why there is no code here that reacts to the weather
/// beyond setting a uniform.
/// </para>
/// </remarks>
public sealed class Rain
{
    /// <summary>Drops the box can hold at once. The buffer, not the rate — see the remarks.</summary>
    /// <remarks>
    /// Sized so a full downpour fills a 48 m box convincingly. A feel constant, and one of the two
    /// numbers M7d should judge against a frame time; the other is <see cref="BoxHalfWidthMetres"/>.
    /// </remarks>
    private const int DropCount = 24_000;


    /// <summary>Half the footprint of the box drops live in.</summary>
    /// <remarks>
    /// <b>24 m against the sky-height field's 64 m half-span, deliberately.</b> Beyond about this
    /// far a drop is thinner than a pixel and contributes haze rather than rain, so widening the
    /// box buys density where it cannot be seen and takes it from where it can. The field is larger
    /// so this can be retuned without anybody having to think about the field.
    /// </remarks>
    private const float BoxHalfWidthMetres = 24f;

    private const float BoxHeightMetres = 32f;


    private readonly GpuParticles3D _drops;
    private readonly ShaderMaterial _dropProcess;
    private readonly ShaderMaterial _dropDraw;
    private readonly QuadMesh _dropMesh;

    public Rain(Node parent)
    {
        System.ArgumentNullException.ThrowIfNull(parent);

        _dropProcess = Load("res://Rendering/Rain.gdshader");
        _dropDraw = Load("res://Rendering/RainDrop.gdshader");

        // A unit quad; the draw shaders build the real geometry in world space from it, so the
        // size here is only the range VERTEX sweeps and must stay 1.
        _dropMesh = new QuadMesh { Size = Vector2.One };
        _dropMesh.SurfaceSetMaterial(0, _dropDraw);



        _drops = new GpuParticles3D
        {
            Name = "Rain",
            Amount = DropCount,

            // The lifetime is a recycle interval rather than a drop's life: the process shader
            // respawns a drop that lands or leaves the box, so a drop only reaches this if it
            // somehow does neither. Long enough that it never fires in practice, short enough
            // that a stuck drop is not stuck forever.
            Lifetime = 12f,
            ProcessMaterial = _dropProcess,
            DrawPass1 = _dropMesh,
            LocalCoords = false,
            Emitting = true,
        };

        parent.AddChild(_drops);

        // After both are in the tree, because the path is resolved against the scene.

        SetIntensity(0f);
    }

    private static ShaderMaterial Load(string path)
    {
        var shader = GD.Load<Shader>(path);
        if (shader is null)
        {
            GD.PushError($"Overburden: could not load {path}");
        }

        return new ShaderMaterial { Shader = shader };
    }

    /// <summary>Whether anything is being drawn at all.</summary>
    public bool Falling { get; private set; }

    /// <summary>
    /// Puts the box where the camera is and tells the shader how hard it is raining.
    /// </summary>
    /// <remarks>
    /// <b>The visibility AABB is set from the box rather than left to Godot.</b> A particle system
    /// whose particles are in world space has no bounds the engine can derive — it would have to
    /// read them back off the GPU — so without this the whole system is culled the moment the
    /// emitter node leaves the frustum, which for a node sitting on the camera means rain that
    /// disappears when you look up.
    /// </remarks>
    public void Update(Vector3 cameraPosition, float intensity, SkyHeightField field)
    {
        System.ArgumentNullException.ThrowIfNull(field);

        // Snapped to a metre. The box only decides where drops are recycled, so moving it
        // continuously buys nothing and makes the uniform change every frame for no visible
        // reason; a metre is far inside the box's own slack.
        var centre = new Vector3(
            Mathf.Round(cameraPosition.X),
            Mathf.Round(cameraPosition.Y),
            Mathf.Round(cameraPosition.Z));

        _drops.GlobalPosition = centre;

        var extents = new Vector3(BoxHalfWidthMetres, BoxHeightMetres, BoxHalfWidthMetres);
        _drops.VisibilityAabb = new Aabb(-extents, extents * 2f);

        _dropProcess.SetShaderParameter("box_height", BoxHeightMetres);

        _dropProcess.SetShaderParameter("sky_height", field.Texture);
        _dropProcess.SetShaderParameter(
            "field_origin", new Vector2(field.OriginX, field.OriginZ));
        _dropProcess.SetShaderParameter("field_span", SkyHeightField.SpanMetres);
        _dropProcess.SetShaderParameter("box_centre", centre);
        _dropProcess.SetShaderParameter("box_half_width", BoxHalfWidthMetres);

        SetIntensity(intensity);
    }

    /// <summary>Dims the rain with the sky, so a storm's drops are not brighter than its ground.</summary>
    public void SetBrightness(float brightness)
    {
        var tint = new Color(0.72f, 0.78f, 0.88f) * Mathf.Clamp(brightness, 0f, 2f);
        _dropDraw.SetShaderParameter("tint", tint);
    }

    private void SetIntensity(float intensity)
    {
        float clamped = Mathf.Clamp(intensity, 0f, 1f);
        _dropProcess.SetShaderParameter("intensity", clamped);

        // Stopped outright rather than left running at zero. An emitting system still steps every
        // particle every frame whether or not any of them are active, and a clear day is most of
        // the time.
        Falling = clamped > 0.001f;
        _drops.Emitting = Falling;
    }

    public void Dispose()
    {
        _drops.QueueFree();
        _dropProcess.Dispose();
        _dropDraw.Dispose();
        _dropMesh.Dispose();
    }
}
