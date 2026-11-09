using UnityEngine;

[CreateAssetMenu(menuName = "CatCooks/Dust and smoke/Surface profile", fileName = "Dust_Profile")]
public sealed class CatCooksDustProfile : ScriptableObject
{
    public enum Shape { Cloud, Round, Wispy, Chunky }
    public enum Pattern { Plain, Patches, Swirls, Speckles, Bands }
    public enum Debris { Pebbles, Splinters, Flakes }
    [Header("Palette — colour over life")]
    public Color colour = new Color(.68f, .55f, .39f, 1);
    public bool usePalette;
    public Color secondaryColour = new Color(.82f,.69f,.48f,1);
    public Color endColour = new Color(.48f,.43f,.38f,1);
    public Color grainColour = new Color(.39f,.30f,.20f,1);
    [Range(0,1)] public float paletteMix = .55f;
    [Range(0,1)] public float colourOverLife = .6f;
    [Range(0,.4f)] public float brightnessVariation = .08f;
    [Header("Volume and lifetime")]
    [Range(.1f, 2)] public float size = 1;
    [Range(.2f, 2)] public float lifetime = 1;
    [Range(0, 2)] public float amount = 1;
    [Range(0, 1)] public float opacity = .85f;
    [Range(0, 1)] public float erosion = .75f;
    public Shape shape;
    [Range(0,.8f)] public float sizeVariation = .25f;
    [Range(.2f,2)] public float verticalStretch = 1;
    [Range(.5f,2)] public float growth = 1;
    [Header("Surface — procedural, no texture needed")]
    public Pattern pattern;
    [Range(0,1)] public float patternStrength = .25f;
    [Range(.5f,12)] public float patternScale = 4;
    [Range(0,2)] public float patternMotion = .35f;
    [Header("Movement")]
    [Range(.2f, 8)] public float drag = 3;
    [Range(0,3)] public float outwardSpeed = 1;
    [Range(0,3)] public float rise = 1;
    [Range(0,3)] public float turbulence = 1;
    [Range(0,3)] public float spin = 1;
    [Header("Secondary debris")]
    [Range(0, 1)] public float grains = .5f;
    public Debris debrisShape;
    [Range(.3f,3)] public float grainSize = 1;
    [Tooltip("Add debris to steps, wheels and smoke too. Impacts always use Grains.")]
    public bool grainsOnAllEffects;
}
