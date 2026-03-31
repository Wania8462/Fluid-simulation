static const int2 offsets2D[9] =
{
	int2(-1, 1),
	int2(0, 1),
	int2(1, 1),
	int2(-1, 0),
	int2(0, 0),
	int2(1, 0),
	int2(-1, -1),
	int2(0, -1),
	int2(1, -1),
};

static const uint hashK1 = 15823;
static const uint hashK2 = 9737333;

int GetHashFromPosition(float2 pos, float cellLength)
{
    int2 cellIndex = (int2)floor(pos / cellLength);
    return (cellIndex.x * hashK1) + (cellIndex.y * hashK2);
}