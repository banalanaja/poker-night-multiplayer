#pragma once
#include <cstddef>
#include <cstdint>
#include <deque>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <thread>

namespace pnm {
inline constexpr std::size_t max_message_bytes = 65536;
inline constexpr std::size_t max_queue_bytes = 1024 * 1024;
class Queue {
    std::mutex mutex_;
    std::deque<std::string> values_;
    std::size_t bytes_ = 0;
public:
    bool push(std::string_view value) {
        if (value.empty() || value.size() > max_message_bytes || value.find('\0') != std::string_view::npos) return false;
        std::lock_guard lock(mutex_);
        if (values_.size() >= 256 || bytes_ + value.size() > max_queue_bytes) return false;
        values_.emplace_back(value);
        bytes_ += value.size();
        return true;
    }
    std::optional<std::string> pop(std::size_t capacity = max_message_bytes) {
        std::lock_guard lock(mutex_);
        if (values_.empty() || values_.front().size() > capacity) return std::nullopt;
        auto value = std::move(values_.front()); values_.pop_front(); bytes_ -= value.size();
        return value;
    }
};
class GameThread {
    const std::thread::id owner_ = std::this_thread::get_id();
public:
    bool current() const noexcept { return owner_ == std::this_thread::get_id(); }
};
}
