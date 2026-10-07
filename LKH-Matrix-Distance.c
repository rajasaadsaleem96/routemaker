#include "LKH.h"
#ifdef _WIN32
#include <windows.h>
#endif

/*
 * Panorra matrix-backed SPECIAL distance.
 *
 * The application supplies:
 *   PANORRA_MATRIX_FILE  = raw row-major little-endian Uint16 matrix
 *   PANORRA_MATRIX_COUNT = matrix dimension
 *   PANORRA_DUMMY_ID     = optional dummy node id for start/end path mode
 *   PANORRA_START_ID     = start node id
 *   PANORRA_END_ID       = end node id
 *
 * For ATSP, LKH calls this function through Distance_Asymmetric/OldDistance,
 * so the original directed matrix entry [from][to] is preserved.
 */

static HANDLE PanorraMatrixFile = INVALID_HANDLE_VALUE;
static HANDLE PanorraMatrixMap = NULL;
static unsigned char *PanorraMatrix = NULL;
static unsigned long long PanorraCount = 0;
static int PanorraDummyId = 0;
static int PanorraStartId = 0;
static int PanorraEndId = 0;
static int PanorraReady = 0;

static void PanorraCloseMatrix(void)
{
#ifdef _WIN32
    if (PanorraMatrix) {
        UnmapViewOfFile(PanorraMatrix);
        PanorraMatrix = NULL;
    }
    if (PanorraMatrixMap) {
        CloseHandle(PanorraMatrixMap);
        PanorraMatrixMap = NULL;
    }
    if (PanorraMatrixFile != INVALID_HANDLE_VALUE) {
        CloseHandle(PanorraMatrixFile);
        PanorraMatrixFile = INVALID_HANDLE_VALUE;
    }
#endif
}

static void PanorraOpenMatrix(void)
{
#ifdef _WIN32
    const char *Path;
    const char *CountText;
    const char *DummyText;
    const char *StartText;
    const char *EndText;
    LARGE_INTEGER Size;

    if (PanorraReady)
        return;
    PanorraReady = 1;

    Path = getenv("PANORRA_MATRIX_FILE");
    CountText = getenv("PANORRA_MATRIX_COUNT");
    DummyText = getenv("PANORRA_DUMMY_ID");
    StartText = getenv("PANORRA_START_ID");
    EndText = getenv("PANORRA_END_ID");

    if (!Path || !CountText)
        eprintf("Panorra matrix environment variables are missing");

    PanorraCount = _strtoui64(CountText, NULL, 10);
    if (PanorraCount == 0)
        eprintf("PANORRA_MATRIX_COUNT is invalid");

    if (DummyText)
        PanorraDummyId = atoi(DummyText);
    if (StartText)
        PanorraStartId = atoi(StartText);
    if (EndText)
        PanorraEndId = atoi(EndText);

    PanorraMatrixFile = CreateFileA(
        Path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (PanorraMatrixFile == INVALID_HANDLE_VALUE)
        eprintf("Cannot open Panorra distance matrix: %s", Path);

    if (!GetFileSizeEx(PanorraMatrixFile, &Size))
        eprintf("Cannot read Panorra matrix file size");

    {
        unsigned long long Expected = PanorraCount * PanorraCount * 2ULL;
        unsigned long long Actual =
            ((unsigned long long) Size.HighPart << 32) |
            (unsigned long long) Size.LowPart;
        if (Actual != Expected)
            eprintf("Panorra matrix size mismatch: expected %llu bytes, got %llu",
                    Expected, Actual);
    }

    PanorraMatrixMap = CreateFileMappingA(
        PanorraMatrixFile, NULL, PAGE_READONLY, 0, 0, NULL);
    if (!PanorraMatrixMap)
        eprintf("Cannot create Panorra matrix mapping");

    PanorraMatrix = (unsigned char *) MapViewOfFile(
        PanorraMatrixMap, FILE_MAP_READ, 0, 0, 0);
    if (!PanorraMatrix)
        eprintf("Cannot map Panorra distance matrix into memory");

    atexit(PanorraCloseMatrix);
#else
    eprintf("Panorra matrix mode is Windows-only in this build");
#endif
}

int Distance_SPECIAL(Node * Na, Node * Nb)
{
    unsigned long long Row, Col, Offset;

    PanorraOpenMatrix();

    /* Optional dummy node converts a Hamiltonian path to a cycle:
       dummy -> start = 0 and end -> dummy = 0; every other dummy edge is huge.
     */
    if (PanorraDummyId &&
        (Na->Id == PanorraDummyId || Nb->Id == PanorraDummyId)) {
        if (Na->Id == PanorraDummyId && Nb->Id == PanorraStartId)
            return 0;
        if (Na->Id == PanorraEndId && Nb->Id == PanorraDummyId)
            return 0;
        return INT_MAX / 4;
    }

    Row = (unsigned long long) (Na->X + 0.5);
    Col = (unsigned long long) (Nb->X + 0.5);

    if (Row < 1 || Col < 1 || Row > PanorraCount || Col > PanorraCount)
        eprintf("Panorra matrix node index out of range");

    Offset = ((Row - 1ULL) * PanorraCount + (Col - 1ULL)) * 2ULL;
    return (int) PanorraMatrix[Offset] |
           ((int) PanorraMatrix[Offset + 1ULL] << 8);
}