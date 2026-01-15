static float PI = 3.1415926;

float SmoothingKernelPoly6(float dist2, float smoothRad, float smoothRad2)
{
    if (dist2 > smoothRad)
        return 0;

    return 315 * pow(abs(smoothRad2 - dist2), 3) / (64 * PI * pow(smoothRad, 9));
}

float Distance(float3 pos1, float3 pos2)
{
    return sqrt(pow(abs(pos1.x - pos2.x), 2) + pow(abs(pos1.y - pos2.y), 2) + pow(abs(pos1.z - pos2.z), 2));
}

float Distance2(float3 pos1, float3 pos2)
{
    return pow(abs(pos1.x - pos2.x), 2) + pow(abs(pos1.y - pos2.y), 2) + pow(abs(pos1.z - pos2.z), 2);
}