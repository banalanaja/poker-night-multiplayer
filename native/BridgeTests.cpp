#include "BridgeQueue.h"
#include <atomic>
#include <iostream>
#include <stdexcept>
#include <vector>
void check(bool condition) { if (!condition) throw std::runtime_error("bridge assertion failed"); }
int main() {
    pnm::Queue queue;
    check(!queue.push(""));
    check(!queue.push(std::string(pnm::max_message_bytes + 1, 'x')));
    check(!queue.push(std::string("a\0b",3)));
    check(queue.push("first")); check(queue.push("second"));
    check(!queue.pop(2)); check(queue.pop().value() == "first"); check(queue.pop().value() == "second"); check(!queue.pop());
    pnm::GameThread thread;
    check(thread.current()); std::atomic<bool> otherThreadAccepted{true};
    std::thread other([&] { otherThreadAccepted = thread.current(); }); other.join(); check(!otherThreadAccepted);
    std::string large(pnm::max_message_bytes,'x');
    for (int i=0; i<16; ++i) check(queue.push(large));
    check(!queue.push("overflow"));
    for (int i=0; i<16; ++i) check(queue.pop().has_value());
    std::atomic<int> inserted{0};
    std::vector<std::thread> writers;
    for(int i=0;i<8;++i) writers.emplace_back([&] { for(int n=0;n<32;++n) if(queue.push("{}")) ++inserted; });
    for(auto& writer:writers) writer.join(); check(inserted == 256); check(!queue.push("{}"));
    for(int i=0;i<256;++i) check(queue.pop().value() == "{}");
    check(!queue.pop());
    std::cout << "Bridge queue, bounds, concurrency and thread-affinity tests passed. Runtime hooks unverified.\n";
}
