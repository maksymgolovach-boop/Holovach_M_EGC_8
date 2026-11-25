#version 330 core

in vec2 vUV;
in vec3 vLocalPos;
in vec3 vWorldNormal;

uniform bool uShowUV;
uniform float uTime;
uniform vec3 uSeed;
uniform float uSeaLevel;
uniform float uNoiseScale;
uniform int uOctaves;
uniform float uIceLatitude;
uniform float uCloudAmount;
uniform float uCloudCover;
uniform float uCloudSpeed;
uniform bool uCityLights;
uniform vec3 uLightDir;

out vec4 FragColor;

float hash(vec3 p)
{
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float valueNoise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);

    float c000 = hash(i);
    float c100 = hash(i + vec3(1.0, 0.0, 0.0));
    float c010 = hash(i + vec3(0.0, 1.0, 0.0));
    float c110 = hash(i + vec3(1.0, 1.0, 0.0));
    float c001 = hash(i + vec3(0.0, 0.0, 1.0));
    float c101 = hash(i + vec3(1.0, 0.0, 1.0));
    float c011 = hash(i + vec3(0.0, 1.0, 1.0));
    float c111 = hash(i + vec3(1.0, 1.0, 1.0));

    float x00 = mix(c000, c100, u.x);
    float x10 = mix(c010, c110, u.x);
    float x01 = mix(c001, c101, u.x);
    float x11 = mix(c011, c111, u.x);

    float y0 = mix(x00, x10, u.y);
    float y1 = mix(x01, x11, u.y);

    return mix(y0, y1, u.z);
}

float fbm(vec3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float total = 0.0;
    for (int i = 0; i < 8; i++)
    {
        if (i >= octaves) break;
        sum += amplitude * valueNoise(p);
        total += amplitude;
        p = p * 2.03 + vec3(1.7, 9.2, 3.1);
        amplitude *= 0.5;
    }
    return sum / max(total, 0.001);
}

float stretch(float x)
{
    return clamp(0.5 + 2.0 * (x - 0.5), 0.0, 1.0);
}

float band(float edge, float x)
{
    float w = max(fwidth(x), 0.004);
    return smoothstep(edge - w, edge + w, x);
}

void main()
{
    if (uShowUV)
    {
        FragColor = vec4(vUV, 0.0, 1.0);
        return;
    }

    vec3 n = normalize(vLocalPos);

    float h = stretch(fbm(n * uNoiseScale + uSeed, uOctaves));

    vec3 deepSea = vec3(0.02, 0.07, 0.22);
    vec3 shallowSea = vec3(0.08, 0.40, 0.62);
    vec3 ocean = mix(deepSea, shallowSea, smoothstep(uSeaLevel - 0.22, uSeaLevel, h));

    float land = (h - uSeaLevel) / max(1.0 - uSeaLevel, 0.001);
    vec3 ground = vec3(0.86, 0.79, 0.56);
    ground = mix(ground, vec3(0.38, 0.62, 0.26), band(0.04, land));
    ground = mix(ground, vec3(0.13, 0.38, 0.16), band(0.22, land));
    ground = mix(ground, vec3(0.42, 0.35, 0.29), band(0.45, land));
    ground = mix(ground, vec3(0.95, 0.96, 0.98), band(0.72, land));

    float isLand = band(uSeaLevel, h);
    vec3 surface = mix(ocean, ground, isLand);

    float iceEdge = uIceLatitude
                  + 0.10 * (valueNoise(n * 6.0 + uSeed * 1.3) - 0.5)
                  + 0.05 * (valueNoise(n * 17.0 - uSeed) - 0.5);
    float ice = band(0.0, abs(n.y) - iceEdge);
    surface = mix(surface, vec3(0.90, 0.94, 1.0), ice);

    float diffuse = dot(normalize(vWorldNormal), normalize(uLightDir));
    vec3 light = vec3(0.16 + 0.84 * max(diffuse, 0.0));
    vec3 color = surface * light;

    float night = 1.0 - smoothstep(-0.25, 0.05, diffuse);
    float cityNoise = valueNoise(n * 45.0 + uSeed * 2.0);
    float cities = smoothstep(0.70, 0.80, cityNoise) * isLand * (1.0 - band(0.45, land))
                 * (1.0 - ice) * night * float(uCityLights);

    float angle = uTime * uCloudSpeed;
    float ca = cos(angle);
    float sa = sin(angle);
    vec3 cn = vec3(ca * n.x - sa * n.z, n.y, sa * n.x + ca * n.z);
    float cloudNoise = stretch(fbm(cn * 3.0 + vec3(31.4, 7.1 + uTime * uCloudSpeed * 0.5, 12.9), 5));
    float clouds = smoothstep(uCloudCover, uCloudCover + 0.2, cloudNoise) * uCloudAmount;

    color += vec3(1.0, 0.82, 0.45) * cities * (1.0 - clouds);
    color = mix(color, light, clouds * 0.9);

    FragColor = vec4(color, 1.0);
}
