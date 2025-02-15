#version 460 core
#extension GL_ARB_bindless_texture : require
#define LOCAL_SIZE_X 8
layout(local_size_x = LOCAL_SIZE_X, local_size_y = 6, local_size_z = 1) in;

struct Frustum
{
	vec4 Planes[6];
};

struct DrawCommand
{
    int Count;
    int InstanceCount;
    int FirstIndex;
    int BaseVertex;
    int BaseInstance;
};

struct Node
{
    vec3 Min;
    uint IsLeafAndVerticesStart;
    vec3 Max;
    uint MissLinkAndVerticesCount;
};

struct Mesh
{
    mat4 Model;
    mat4 PrevModel;
    int MaterialIndex;
    int BaseNode;
    int _pad0;
    int _pad1;
};

struct PointShadow
{
    samplerCubeShadow Sampler;
    float NearPlane;
    float FarPlane;

    mat4 ProjViewMatrices[6];

    vec3 _pad0;
    int LightIndex;
};

layout(std430, binding = 0) restrict writeonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandsSSBO;

layout(std430, binding = 1) restrict readonly buffer BVHSSBO
{
    vec3 _pad0;
    uint TreeDepth;
    Node Nodes[];
} bvhSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[8];
    int PointCount;
} shadowDataUBO;

Frustum ExtractFrustum(mat4 projViewModel);
bool AABBVsFrustum(Frustum frustum, Node node);
vec3 NegativeVertex(Node node, vec3 normal);

layout(location = 0) uniform int ShadowIndex;

// 1. Count number of shadow-cubemap-faces the mesh is visible from the shadow source
// 2. Pack each visible face into a single int
// 3. Write the packed int into the BaseInstance draw command paramter. The shadow vertex shader will access this variable
// 4. Also write the InstanceCount into the draw command buffer - one instance for each mesh

// Note: Meshes are processed in batches of LOCAL_SIZE_X Threads. Additionaly each mesh gets processed by 6 Threads one for each face.

shared int SharedPackedValues[LOCAL_SIZE_X];
shared int SharedInstanceCounts[LOCAL_SIZE_X];
void main()
{
    const uint globalMeshIndex = gl_GlobalInvocationID.x;
    if (globalMeshIndex >= meshSSBO.Meshes.length())
        return;

    const int cubemapFace = int(gl_LocalInvocationID.y);
    const int localMeshIndex = int(gl_LocalInvocationID.x);

    SharedPackedValues[localMeshIndex] = 0;
    SharedInstanceCounts[localMeshIndex] = 0;

    Mesh mesh = meshSSBO.Meshes[globalMeshIndex];
    Node node = bvhSSBO.Nodes[mesh.BaseNode];
    Frustum frustum = ExtractFrustum(shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[cubemapFace] * mesh.Model);

    memoryBarrierShared();

    if (AABBVsFrustum(frustum, node))
    {
        // Basically a atomic bitfieldInsert()
        atomicOr(SharedPackedValues[localMeshIndex], cubemapFace << (3 * atomicAdd(SharedInstanceCounts[localMeshIndex], 1)));
    }

    if (cubemapFace == 0)
    {
        memoryBarrierShared();
        drawCommandsSSBO.DrawCommands[globalMeshIndex].InstanceCount = SharedInstanceCounts[localMeshIndex];
        drawCommandsSSBO.DrawCommands[globalMeshIndex].BaseInstance = SharedPackedValues[localMeshIndex];
    }
}

Frustum ExtractFrustum(mat4 projViewModel)
{
    Frustum frustum;
	for (int i = 0; i < 3; i++)
    {
        for (int j = 0; j < 2; j++)
        {
            frustum.Planes[i * 2 + j].x = projViewModel[0][3] + (j == 0 ? projViewModel[0][i] : -projViewModel[0][i]);
            frustum.Planes[i * 2 + j].y = projViewModel[1][3] + (j == 0 ? projViewModel[1][i] : -projViewModel[1][i]);
            frustum.Planes[i * 2 + j].z = projViewModel[2][3] + (j == 0 ? projViewModel[2][i] : -projViewModel[2][i]);
            frustum.Planes[i * 2 + j].w = projViewModel[3][3] + (j == 0 ? projViewModel[3][i] : -projViewModel[3][i]);
            frustum.Planes[i * 2 + j] *= length(frustum.Planes[i * 2 + j].xyz);
        }
    }
	return frustum;
}

bool AABBVsFrustum(Frustum frustum, Node node)
{
	float a = 1.0;

	for (int i = 0; i < 6 && a >= 0.0; i++) {
		vec3 negative = NegativeVertex(node, frustum.Planes[i].xyz);

		a = dot(vec4(negative, 1.0), frustum.Planes[i]);
	}

	return a >= 0.0;
}

vec3 NegativeVertex(Node node, vec3 normal)
{
	return mix(node.Min, node.Max, greaterThan(normal, vec3(0.0)));
}