#ifdef _WIN32
#include <windows.h>

double GetTime(void)
{
    static LARGE_INTEGER Frequency;
    LARGE_INTEGER Counter;

    if (Frequency.QuadPart == 0)
        QueryPerformanceFrequency(&Frequency);
    QueryPerformanceCounter(&Counter);
    return (double) Counter.QuadPart / (double) Frequency.QuadPart;
}

#else
#include <time.h>
double GetTime(void)
{
    return (double) clock() / CLOCKS_PER_SEC;
}
#endif
