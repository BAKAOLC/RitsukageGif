"""向 RitsukageGif framed TCP 输入发送生成画面和 JSON；只使用 Python 标准库。"""

import argparse
import json
import math
import socket
import struct
import time


HEADER = struct.Struct('<4sBBHIIIId')
GREETING = struct.Struct('<4sId')


def receive_exact(connection, length):
    data = bytearray()
    while len(data) < length:
        block = connection.recv(length - len(data))
        if not block:
            raise ConnectionError('接收握手时连接已关闭')
        data.extend(block)
    return data


def send_packet(connection, kind, payload, timestamp, width=0, height=0):
    connection.sendall(HEADER.pack(b'RGIP', 1, kind, 0, len(payload),
                                   width, height, 0, timestamp))
    connection.sendall(payload)


def make_frame(width, height, elapsed):
    # BGRA，自上而下紧密排列；演示画面所有像素均不透明。
    pixels = bytearray(bytes((43, 32, 24, 255)) * (width * height))
    side = max(1, min(width, height) // 5)
    x = round((width - side) * (0.5 + 0.5 * math.sin(elapsed)))
    y = round((height - side) * (0.5 + 0.5 * math.cos(elapsed * 0.7)))
    row = bytes((237, 168, 81, 255)) * side
    for line in range(y, y + side):
        start = (line * width + x) * 4
        pixels[start:start + len(row)] = row
    return pixels, {'x': x, 'y': y, 'width': side, 'height': side}


def parse_endpoint(text):
    host, separator, port = text.rpartition(':')
    if not separator:
        raise argparse.ArgumentTypeError('地址应为 127.0.0.1:端口')
    host = host.strip('[]')
    try:
        number = int(port)
    except ValueError as error:
        raise argparse.ArgumentTypeError('端口必须为整数') from error
    if host not in ('127.0.0.1', '::1', 'localhost') or not 1 <= number <= 65535:
        raise argparse.ArgumentTypeError('请使用本机回环地址和 1～65535 的端口')
    return host, number


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('endpoint', type=parse_endpoint, help='input listen 返回的 TCP 地址')
    parser.add_argument('--mode', choices=('frames', 'json', 'both'), default='both')
    parser.add_argument('--width', type=int, default=320)
    parser.add_argument('--height', type=int, default=180)
    parser.add_argument('--fps', type=float, default=30)
    parser.add_argument('--seconds', type=float, default=0, help='0 表示持续发送，Ctrl+C 结束')
    args = parser.parse_args()
    if (not 1 <= args.width <= 16384 or not 1 <= args.height <= 16384
            or args.width * args.height * 4 > 64 * 1024 * 1024
            or not math.isfinite(args.fps) or not 0 < args.fps <= 240
            or not math.isfinite(args.seconds) or args.seconds < 0):
        parser.error('尺寸、帧率或持续时间超出范围')
    sent = 0
    try:
        with socket.create_connection(args.endpoint, timeout=10) as connection:
            connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            magic, version, server_time = GREETING.unpack(receive_exact(connection, 16))
            received_at = time.perf_counter()
            if magic != b'RGIH' or version != 1 or not math.isfinite(server_time):
                raise ValueError('不支持的 RitsukageGif 输入握手')
            # 将本地相对时间映射到服务端单调时钟；握手传输延迟仍会形成偏差。
            deadline = received_at
            while True:
                now = time.perf_counter()
                elapsed = now - received_at
                if args.seconds and elapsed >= args.seconds:
                    break
                timestamp = server_time + elapsed
                frame, target = make_frame(args.width, args.height, elapsed)
                if args.mode != 'json':
                    send_packet(connection, 1, frame, timestamp, args.width, args.height)
                if args.mode != 'frames':
                    payload = json.dumps({'frame': sent, 'elapsed': elapsed, 'target': target},
                                         separators=(',', ':'), allow_nan=False).encode('utf-8')
                    send_packet(connection, 2, payload, timestamp)
                sent += 1
                period = 1 / args.fps
                deadline = max(deadline + period, time.perf_counter())
                time.sleep(max(0, deadline - time.perf_counter()))
    except KeyboardInterrupt:
        pass
    except (OSError, ValueError, ConnectionError) as error:
        parser.exit(1, f'发送失败：{error}\n')
    print(f'已发送 {sent} 次更新')


if __name__ == '__main__':
    main()
