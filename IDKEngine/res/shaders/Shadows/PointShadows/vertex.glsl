#version 460 core
#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_AMD_vertex_shader_layer : enable
#extension GL_NV_viewport_array : enable
#extension GL_NV_viewport_array2 : enable

#define IS_VERTEX_LAYERED_RENDERING defined(GL_ARB_shader_viewport_layer_array) || defined(GL_AMD_vertex_shader_layer) || defined(GL_NV_viewport_array) || defined(GL_NV_viewport_array2)

layout(location = 0) in vec3 Position;

struct PointShadow
{
    samplerCubeShadow Sampler;
    float NearPlane;
    float FarPlane;

    mat4 ProjViewMatrices[6];

    vec3 _pad0;
    int LightIndex;
};

struct AABB
{
    vec3 Min;
    int PTVerticesStart;

    vec3 Max;
    int PTVerticesEnd;
};

struct Mesh
{
    mat4 Model[1];
    int MaterialIndex;
    int BVHEntry;
    int _pad0;
    int _pad1;
};

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[8];
    int PointCount;
} shadowDataUBO;

out InOutVars
{
    vec3 FragPos;
} outData;

layout(location = 0) uniform int Index;
layout(location = 1) uniform int Layer;

void main()
{
#if IS_VERTEX_LAYERED_RENDERING

    gl_Layer = gl_InstanceID;
    
    mat4 model = meshSSBO.Meshes[gl_DrawID].Model[0];
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[Index].ProjViewMatrices[gl_Layer] * vec4(outData.FragPos, 1.0);

#else

    mat4 model = meshSSBO.Meshes[gl_DrawID].Model[0];
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[Index].ProjViewMatrices[Layer] * vec4(outData.FragPos, 1.0);

#endif
}