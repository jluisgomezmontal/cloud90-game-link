#include <windows.h>
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

namespace {
namespace fs = std::filesystem;

struct HashAlgorithm {
    BCRYPT_ALG_HANDLE handle = nullptr;
    ~HashAlgorithm() { if (handle) BCryptCloseAlgorithmProvider(handle, 0); }
};

struct HashState {
    BCRYPT_HASH_HANDLE handle = nullptr;
    ~HashState() { if (handle) BCryptDestroyHash(handle); }
};

std::string ExecutableHash(const fs::path& path) {
    std::ifstream executable(path, std::ios::binary);
    if (!executable) return {};

    HashAlgorithm algorithm;
    if (BCryptOpenAlgorithmProvider(&algorithm.handle, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
        return {};

    DWORD stateSize = 0;
    DWORD bytesWritten = 0;
    if (BCryptGetProperty(algorithm.handle, BCRYPT_OBJECT_LENGTH,
                          reinterpret_cast<PUCHAR>(&stateSize), sizeof(stateSize), &bytesWritten, 0) < 0)
        return {};

    std::vector<UCHAR> storage(stateSize);
    HashState state;
    if (BCryptCreateHash(algorithm.handle, &state.handle, storage.data(), stateSize, nullptr, 0, 0) < 0)
        return {};

    std::vector<UCHAR> chunk(128 * 1024);
    while (executable.read(reinterpret_cast<char*>(chunk.data()), chunk.size()) || executable.gcount()) {
        if (BCryptHashData(state.handle, chunk.data(), static_cast<ULONG>(executable.gcount()), 0) < 0)
            return {};
    }
    if (!executable.eof() || executable.bad()) return {};

    UCHAR digest[32];
    if (BCryptFinishHash(state.handle, digest, sizeof(digest), 0) < 0)
        return {};

    const char* hex = "0123456789abcdef";
    std::string result;
    for (const auto byte : digest) {
        result += hex[byte >> 4];
        result += hex[byte & 15];
    }
    return result;
}

std::string ReadSession(const fs::path& root) {
    std::ifstream pointer(root / L"active-session.txt", std::ios::binary);
    char buffer[33];
    pointer.read(buffer, sizeof(buffer));
    if (pointer.gcount() != 32 || pointer.bad()) return {};
    const std::string session(buffer, 32);
    if (session.find_first_not_of("0123456789abcdef") != std::string::npos) return {};
    return session;
}

void Publish(const fs::path& root, const std::string& session, const std::string& hash) {
    const auto directory = root / session;
    const auto destination = directory / L"status.json";
    const auto temporary = directory / (L"status-" + std::to_wstring(GetCurrentProcessId()) + L".tmp");
    std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
    if (!output) return;
    output << "{\"session\":\"" << session << "\",\"epoch\":\"" << session
           << "_probe\",\"bridgeVersion\":\"cloud90-tlou2-1\",\"pid\":" << GetCurrentProcessId()
           << ",\"gameSha256\":\"" << hash << "\",\"heartbeatMs\":" << GetTickCount64()
           << ",\"mode\":\"unknown\",\"inPlay\":false,\"capabilities\":[],\"acks\":[]}";
    output.close();
    if (output && ReadSession(root) == session &&
        MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING))
        return;
    DeleteFileW(temporary.c_str());
}

void RecordLoad(const fs::path& root, const std::string& hash, bool alternateExecutable) {
    fs::create_directories(root);
    const auto pid = GetCurrentProcessId();
    const auto destination = root / (L"probe-" + std::to_wstring(pid) + L".json");
    const auto temporary = root / (L"probe-" + std::to_wstring(pid) + L".tmp");
    std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
    if (!output) return;

    output << "{\"pid\":" << pid << ",\"executable\":\""
           << (alternateExecutable ? "tlou-ii-l.exe" : "tlou-ii.exe")
           << "\",\"gameSha256\":\"" << hash << "\"}";
    output.close();
    if (!output || !MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_REPLACE_EXISTING))
        DeleteFileW(temporary.c_str());
}

DWORD WINAPI RunProbe(void*) {
    try {
        std::wstring executable(32768, L'\0');
        const auto length = GetModuleFileNameW(nullptr, executable.data(),
                                               static_cast<DWORD>(executable.size()));
        if (!length || length >= executable.size()) return 0;
        executable.resize(length);
        const fs::path path(executable);
        const auto filename = path.filename();
        const bool alternateExecutable = _wcsicmp(filename.c_str(), L"tlou-ii-l.exe") == 0;
        if (!alternateExecutable && _wcsicmp(filename.c_str(), L"tlou-ii.exe") != 0) return 0;

        std::wstring localData(32768, L'\0');
        const auto dataLength = GetEnvironmentVariableW(L"LOCALAPPDATA", localData.data(),
                                                       static_cast<DWORD>(localData.size()));
        if (!dataLength || dataLength >= localData.size()) return 0;
        localData.resize(dataLength);

        const auto hash = ExecutableHash(path);
        if (hash.empty()) return 0;
        const auto root = fs::path(localData) / L"Cloud90GameLink" / L"tlou2";
        RecordLoad(root, hash, alternateExecutable);
        if (alternateExecutable) return 0;

        for (;;) {
            const auto session = ReadSession(root);
            if (!session.empty()) Publish(root, session, hash);
            Sleep(250);
        }
    } catch (...) {
        return 0;
    }
}
}

BOOL WINAPI DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason != DLL_PROCESS_ATTACH) return TRUE;

    HMODULE pinned = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                            reinterpret_cast<LPCWSTR>(module), &pinned))
        return FALSE;

    const auto worker = CreateThread(nullptr, 0, RunProbe, nullptr, 0, nullptr);
    if (!worker) return FALSE;
    CloseHandle(worker);
    return TRUE;
}
