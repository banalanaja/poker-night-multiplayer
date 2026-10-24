#include "BridgeQueue.h"
#include <cstring>

#ifdef _WIN32
#define PNM_EXPORT extern "C" __declspec(dllexport)
#else
#define PNM_EXPORT extern "C" __attribute__((visibility("default")))
#endif

// IPC queue primitives only. This DLL has no loader, no injected entry point and no verified Lua hooks yet.
// Polling the command queue must be called by a future verified engine-thread callback.
namespace { pnm::Queue commands; pnm::Queue events; }
PNM_EXPORT int PNM_ProtocolVersion() { return 1; }
PNM_EXPORT int PNM_RuntimeReady() { return 0; }
PNM_EXPORT int PNM_EnqueueCommand(const char* data, std::size_t length) {
    if (!data) return 0;
    return commands.push(std::string_view(data, length)) ? 1 : 0;
}
PNM_EXPORT std::size_t PNM_ReadEvent(char* buffer, std::size_t capacity) {
    if (!buffer || capacity == 0) return 0;
    auto message = events.pop(capacity);
    if (!message) return 0;
    std::memcpy(buffer, message->data(), message->size()); return message->size();
}
