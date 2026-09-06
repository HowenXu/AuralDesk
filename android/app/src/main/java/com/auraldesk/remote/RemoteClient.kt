package com.auraldesk.remote

import android.util.Log
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.net.Inet4Address
import java.net.URL
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

data class RemoteStatus(
    val title: String,
    val singer: String,
    val source: String,
    val mid: String,
    val lang: String,
    val position: Double,
    val length: Double,
    val playing: Boolean
)

/** 与 AuralDesk 电脑端通信：UDP 自动发现 + HTTP 控制。 */
object RemoteClient {
    private const val TAG = "AuralDeskRemote"

    const val HTTP_PORT = 38570
    const val DISCOVER_PORT = 38571

    @Volatile
    var baseUrl: String = ""

    /** 局域网广播发现，返回 http://ip:port，失败返回 null。 */
    fun discover(timeoutMs: Int = 3000): String? {
        val socket = DatagramSocket()
        socket.soTimeout = 600
        val msg = "AURALDESK_DISCOVER".toByteArray()
        val targets = mutableListOf<String>()
        targets.add("255.255.255.255")
        val localIp = localIpv4()
        if (localIp != null) {
            val parts = localIp.split(".")
            if (parts.size == 4) {
                targets.add("${parts[0]}.${parts[1]}.${parts[2]}.255")
            }
        }
        val deadline = System.currentTimeMillis() + timeoutMs
        // 广播可能被路由/AP 拦截，多轮发送 + 轮询接收提高成功率
        for (round in 0 until 3) {
            for (addr in targets) {
                try {
                    socket.send(DatagramPacket(msg, msg.size, InetAddress.getByName(addr), DISCOVER_PORT))
                } catch (_: Exception) { }
            }
            val waitUntil = Math.min(System.currentTimeMillis() + 700, deadline)
            while (System.currentTimeMillis() < waitUntil) {
                val buf = ByteArray(256)
                val recv = DatagramPacket(buf, buf.size)
                try {
                    socket.receive(recv)
                } catch (_: Exception) {
                    break
                }
                val text = String(buf, 0, recv.length, Charsets.UTF_8).trim()
                val m = Regex("AURALDESK_RESPONSE\\s+(\\d+)").find(text)
                if (m != null) {
                    val host = recv.address.hostAddress ?: continue
                    Log.d(TAG, "discover found $host:${m.groupValues[1]}")
                    socket.close()
                    return "http://$host:${m.groupValues[1]}"
                }
            }
        }
        socket.close()
        Log.d(TAG, "discover broadcast timeout, trying TCP scan")
        return tcpScan()
    }

    /** 广播被路由拦截时的兜底：TCP 直连同网段主机的 38570 端口。 */
    private fun tcpScan(): String? {
        return try {
            val localIp = localIpv4() ?: return null
            val parts = localIp.split(".")
            if (parts.size != 4) return null
            val prefix = "${parts[0]}.${parts[1]}.${parts[2]}."
            val found = AtomicReference<String?>()
            val executor = Executors.newFixedThreadPool(24)
            val latch = CountDownLatch(254)
            for (i in 1..254) {
                executor.submit {
                    try {
                        val addr = "$prefix$i"
                        val s = Socket()
                        s.connect(InetSocketAddress(addr, HTTP_PORT), 250)
                        s.close()
                        found.compareAndSet(null, "http://$addr:$HTTP_PORT")
                    } catch (_: Exception) {
                    } finally {
                        latch.countDown()
                    }
                }
            }
            latch.await(3, TimeUnit.SECONDS)
            executor.shutdownNow()
            val f = found.get()
            if (f != null) Log.d(TAG, "tcp scan found $f")
            f
        } catch (_: Exception) {
            null
        }
    }

    /** 枚举网络接口拿到真实 IPv4（Android 的 getLocalHost 常返回 127.0.0.1）。 */
    private fun localIpv4(): String? {
        return try {
            val nis = NetworkInterface.getNetworkInterfaces()
            while (nis.hasMoreElements()) {
                val ni = nis.nextElement()
                if (!ni.isUp || ni.isLoopback) continue
                val addrs = ni.inetAddresses
                while (addrs.hasMoreElements()) {
                    val a = addrs.nextElement()
                    if (a is Inet4Address && !a.isLoopbackAddress) {
                        val ip = a.hostAddress ?: continue
                        if (ip.startsWith("192.168.") || ip.startsWith("10.") || ip.startsWith("172.")) return ip
                    }
                }
            }
            null
        } catch (_: Exception) {
            null
        }
    }

    fun control(action: String) {
        if (baseUrl.isEmpty()) return
        try {
            Log.d(TAG, "control -> $action")
            val conn = URL(baseUrl + "/api/" + action).openConnection() as HttpURLConnection
            conn.requestMethod = "POST"
            conn.connectTimeout = 3000
            conn.readTimeout = 3000
            conn.inputStream.close()
        } catch (e: Exception) {
            Log.e(TAG, "control fail: $e")
        }
    }

    /** 轻量播放状态（控制中心媒体卡片轮询用）。 */
    fun getStatus(): RemoteStatus? {
        if (baseUrl.isEmpty()) return null
        return try {
            Log.d(TAG, "getStatus -> $baseUrl/api/status")
            val conn = URL(baseUrl + "/api/status").openConnection() as HttpURLConnection
            conn.connectTimeout = 3000
            conn.readTimeout = 3000
            val j = JSONObject(conn.inputStream.bufferedReader().use { it.readText() })
            RemoteStatus(
                title = optString(j, "Title", "title"),
                singer = optString(j, "Singer", "singer"),
                source = optString(j, "Source", "source"),
                mid = optString(j, "Mid", "mid"),
                lang = optString(j, "Lang", "lang"),
                position = optDouble(j, "Position", "position"),
                length = optDouble(j, "Length", "length"),
                playing = optBool(j, "Playing", "playing")
            )
        } catch (e: Exception) {
            Log.e(TAG, "getStatus fail: $e")
            null
        }
    }

    fun seek(position: Double) {
        if (baseUrl.isEmpty()) return
        try {
            Log.d(TAG, "seek -> $position")
            val conn = URL(baseUrl + "/api/seek").openConnection() as HttpURLConnection
            conn.requestMethod = "POST"
            conn.doOutput = true
            conn.setRequestProperty("Content-Type", "application/json")
            conn.outputStream.use { it.write("{\"position\":$position}".toByteArray()) }
            conn.inputStream.close()
        } catch (e: Exception) {
            Log.e(TAG, "seek fail: $e")
        }
    }

    private fun optString(j: JSONObject, vararg keys: String): String {
        for (k in keys) {
            val v = j.optString(k, "")
            if (v.isNotEmpty()) return v
        }
        return ""
    }

    private fun optDouble(j: JSONObject, vararg keys: String): Double {
        for (k in keys) {
            if (j.has(k)) return j.optDouble(k, 0.0)
        }
        return 0.0
    }

    private fun optBool(j: JSONObject, vararg keys: String): Boolean {
        for (k in keys) {
            if (j.has(k)) return j.optBoolean(k, false)
        }
        return false
    }

    fun qqState(qstart: Int = 0, qcount: Int = 30): JSONObject? {
        if (baseUrl.isEmpty()) return null
        return try {
            val conn = URL("$baseUrl/api/qq/state?qstart=$qstart&qcount=$qcount").openConnection() as HttpURLConnection
            conn.connectTimeout = 3000
            conn.readTimeout = 3000
            JSONObject(conn.inputStream.bufferedReader().use { it.readText() })
        } catch (e: Exception) {
            Log.e(TAG, "qqState fail: $e")
            null
        }
    }

    fun qqAction(map: Map<String, Any?>) {
        if (baseUrl.isEmpty()) return
        try {
            Log.w(TAG, "qqAction -> $map")
            val conn = URL(baseUrl + "/api/qq/action").openConnection() as HttpURLConnection
            conn.requestMethod = "POST"
            conn.doOutput = true
            conn.setRequestProperty("Content-Type", "application/json")
            val body = JSONObject(map).toString()
            conn.outputStream.use { it.write(body.toByteArray()) }
            val code = conn.responseCode
            Log.w(TAG, "qqAction code=$code")
            conn.inputStream.close()
        } catch (e: Exception) {
            Log.e(TAG, "qqAction fail: $e")
        }
    }
}
