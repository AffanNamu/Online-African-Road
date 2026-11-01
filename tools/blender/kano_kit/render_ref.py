"""Rendering for review: Cycles (CPU) with a Nishita sky and sun, one reference camera and the orthographic/street views the owner asked for. The renders are PREVIEWS of Blender geometry,
never proof that an asset works in Unity."""
from __future__ import annotations
import math
import bpy
from mathutils import Vector


def world(sun_elev_deg=38.0, sun_rot_deg=200.0, strength=1.0, air=1.0, dust=1.2, clouds=True):
    """Lighting from a Nishita sky and sun; what the CAMERA sees is a saturated blue gradient with clouds (so the exposure that suits the street does not blow the sky out). Review renders only."""
    w = bpy.data.worlds.new("kano_sky"); bpy.context.scene.world = w; w.use_nodes = True; nt = w.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    sky = nt.nodes.new("ShaderNodeTexSky"); sky.sky_type = "MULTIPLE_SCATTERING"; sky.sun_elevation = math.radians(sun_elev_deg); sky.sun_rotation = math.radians(sun_rot_deg)
    sky.air_density = air; sky.aerosol_density = dust; sky.sun_size = math.radians(0.6); sky.sun_intensity = 1.0
    bg1 = nt.nodes.new("ShaderNodeBackground"); bg1.inputs["Strength"].default_value = strength; nt.links.new(sky.outputs["Color"], bg1.inputs["Color"])
    tc = nt.nodes.new("ShaderNodeTexCoord"); sep = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(tc.outputs["Generated"], sep.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.color_ramp.elements[0].position = 0.50; ramp.color_ramp.elements[0].color = (0.62, 0.80, 0.98, 1)
    ramp.color_ramp.elements[1].position = 0.78; ramp.color_ramp.elements[1].color = (0.05, 0.28, 0.92, 1); nt.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
    cl_col = ramp.outputs["Color"]
    if clouds:
        noise = nt.nodes.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 3.2; noise.inputs["Detail"].default_value = 6.0; noise.inputs["Roughness"].default_value = 0.6
        mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (1.0, 1.0, 3.2); nt.links.new(tc.outputs["Generated"], mp.inputs["Vector"]); nt.links.new(mp.outputs["Vector"], noise.inputs["Vector"])
        cr = nt.nodes.new("ShaderNodeValToRGB"); cr.color_ramp.elements[0].position = 0.52; cr.color_ramp.elements[0].color = (0, 0, 0, 1); cr.color_ramp.elements[1].position = 0.68; cr.color_ramp.elements[1].color = (1, 1, 1, 1)
        nt.links.new(noise.outputs["Fac"], cr.inputs["Fac"]); mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.inputs[7].default_value = (1.0, 1.0, 1.0, 1)
        nt.links.new(cr.outputs["Color"], mix.inputs[0]); nt.links.new(ramp.outputs["Color"], mix.inputs[6]); cl_col = mix.outputs[2]
    bg2 = nt.nodes.new("ShaderNodeBackground"); bg2.inputs["Strength"].default_value = 7.2   # camera-ray sky only; doubled when the review exposure dropped one stop, so the sky stays the saturated blue of the concept; nt.links.new(cl_col, bg2.inputs["Color"])
    lp = nt.nodes.new("ShaderNodeLightPath"); ms = nt.nodes.new("ShaderNodeMixShader"); nt.links.new(lp.outputs["Is Camera Ray"], ms.inputs[0]); nt.links.new(bg1.outputs["Background"], ms.inputs[1]); nt.links.new(bg2.outputs["Background"], ms.inputs[2])
    out = nt.nodes.new("ShaderNodeOutputWorld"); nt.links.new(ms.outputs["Shader"], out.inputs["Surface"])


def camera(loc, look_at, lens=28.0, name="ref_cam", ortho_scale=None, sensor=36.0):
    cam = bpy.data.cameras.new(name); cam.lens = lens; cam.sensor_width = sensor; cam.clip_start = 0.1; cam.clip_end = 2000
    if ortho_scale: cam.type = "ORTHO"; cam.ortho_scale = ortho_scale
    o = bpy.data.objects.new(name, cam); bpy.context.scene.collection.objects.link(o); o.location = Vector(loc)
    d = Vector(look_at) - Vector(loc); o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler(); bpy.context.scene.camera = o; return o


def render(path: str, width=1280, height=720, samples=48, engine="cycles", denoise=True, exposure=0.0):
    sc = bpy.context.scene; sc.render.resolution_x = width; sc.render.resolution_y = height; sc.render.resolution_percentage = 100
    if engine == "cycles":
        sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = samples; sc.cycles.use_denoising = denoise; sc.cycles.max_bounces = 4; sc.cycles.diffuse_bounces = 2; sc.cycles.glossy_bounces = 2
        sc.cycles.transmission_bounces = 4; sc.cycles.transparent_max_bounces = 4
        try: sc.cycles.denoiser = "OPENIMAGEDENOISE"
        except Exception: pass
    else: sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.view_settings.view_transform = "Standard"; sc.view_settings.exposure = exposure
    try: sc.view_settings.look = "None"
    except Exception: pass
    sc.render.image_settings.file_format = "PNG"; sc.render.filepath = path; bpy.ops.render.render(write_still=True); return path
