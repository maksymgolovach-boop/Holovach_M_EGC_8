#version 330 core

in vec2 vUV;
in vec3 vViewNormal;

uniform bool uShowUV;
uniform vec3 uAtmosphereColor;
uniform float uAtmosphereStrength;

out vec4 FragColor;

void main()
{
    if (uShowUV)
    {
        FragColor = vec4(vUV, 0.0, 0.35);
        return;
    }

    vec3 n = normalize(vViewNormal);
    float rim = 1.0 - abs(n.z);
    float alpha = clamp(pow(rim, 3.0) * uAtmosphereStrength, 0.0, 1.0);
    FragColor = vec4(uAtmosphereColor, alpha);
}
