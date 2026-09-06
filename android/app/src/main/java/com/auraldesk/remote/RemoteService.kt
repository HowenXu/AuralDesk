package com.auraldesk.remote

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.support.v4.media.MediaMetadataCompat
import android.support.v4.media.session.MediaSessionCompat
import android.support.v4.media.session.PlaybackStateCompat
import androidx.core.app.NotificationCompat
import androidx.media.app.NotificationCompat.MediaStyle
import kotlin.concurrent.thread

/**
 * 媒体控制前台服务：持有 MediaSession + MediaStyle 通知，
 * 通知栏显示当前播放信息与控制按钮（播放/暂停/上下曲），进度随电脑端同步。
 * 播放实际发生在电脑端，这里只做状态镜像与控制转发。
 */
class RemoteService : Service() {
    companion object {
        const val CHANNEL_ID = "auraldesk_media"
        const val NOTIF_ID = 2001
        const val ACTION_CONNECT = "com.auraldesk.remote.CONNECT"
        const val ACTION_DISCONNECT = "com.auraldesk.remote.DISCONNECT"
        const val ACTION_REFRESH = "com.auraldesk.remote.REFRESH"
        const val ACTION_PLAY = "com.auraldesk.remote.PLAY"
        const val ACTION_NEXT = "com.auraldesk.remote.NEXT"
        const val ACTION_PREV = "com.auraldesk.remote.PREV"
        const val ACTION_FAV = "com.auraldesk.remote.FAV"
        const val EXTRA_URL = "url"
    }

    private lateinit var session: MediaSessionCompat
    private val handler = Handler(Looper.getMainLooper())
    @Volatile private var polling = false
    private var lastTitle = ""
    private var lastSinger = ""
    @Volatile
    private var lastMid = ""
    private var lastLenMs = -1L
    private var lastPlaying = false
    private var pollCount = 0

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createChannel()
        session = MediaSessionCompat(this, "AuralDeskRemote")
        session.setFlags(
            MediaSessionCompat.FLAG_HANDLES_MEDIA_BUTTONS or
                MediaSessionCompat.FLAG_HANDLES_TRANSPORT_CONTROLS
        )
        session.setCallback(object : MediaSessionCompat.Callback() {
            override fun onPlay() {
                thread { RemoteClient.control("toggle") }
            }
            override fun onPause() {
                thread { RemoteClient.control("toggle") }
            }
            override fun onSkipToNext() {
                thread { RemoteClient.control("next") }
            }
            override fun onSkipToPrevious() {
                thread { RemoteClient.control("prev") }
            }
            override fun onSeekTo(pos: Long) {
                session.setPlaybackState(buildPlaybackState(true, pos, 1.0f))
                thread { RemoteClient.seek(pos / 1000.0) }
            }
            override fun onStop() = stopSelf()
        })
        session.isActive = true
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_CONNECT -> {
                intent.getStringExtra(EXTRA_URL)?.let { RemoteClient.baseUrl = it }
                if (RemoteClient.baseUrl.isNotEmpty()) {
                    postNotification("未在播放", "", playing = false)
                    startPolling()
                }
            }
            ACTION_DISCONNECT -> stopSelf()
            ACTION_REFRESH -> {
                // 前台恢复/主动刷新：重置记忆状态，让下一轮轮询强制重建通知
                lastTitle = ""
                lastSinger = ""
                lastLenMs = -1L
                lastPlaying = false
                if (RemoteClient.baseUrl.isNotEmpty()) startPolling()
            }
            ACTION_PLAY -> thread { RemoteClient.control("toggle") }
            ACTION_NEXT -> thread { RemoteClient.control("next") }
            ACTION_PREV -> thread { RemoteClient.control("prev") }
            ACTION_FAV -> thread {
                if (lastMid.isNotEmpty())
                    RemoteClient.qqAction(mapOf("action" to "toggleFav", "mid" to lastMid))
            }
            else -> {
                if (RemoteClient.baseUrl.isNotEmpty()) {
                    postNotification("未在播放", "", playing = false)
                    startPolling()
                } else {
                    stopSelf()
                }
            }
        }
        return START_NOT_STICKY
    }

    private fun startPolling() {
        if (polling) return
        polling = true
        Thread {
            while (polling) {
                val st = RemoteClient.getStatus()
                handler.post {
                    if (!polling) return@post
                    if (st != null) {
                        val lenMs = (st.length * 1000).toLong()
                        val metaChanged = st.title != lastTitle || st.singer != lastSinger || lenMs != lastLenMs || st.mid != lastMid
                        val playingChanged = st.playing != lastPlaying
                        if (metaChanged || playingChanged) {
                            lastTitle = st.title
                            lastSinger = st.singer
                            lastMid = st.mid
                            lastLenMs = lenMs
                            lastPlaying = st.playing
                            session.setMetadata(buildMetadata(st))
                            postNotification(st.title.ifEmpty { "未在播放" }, st.singer, st.playing)
                        }
                        // 每 30 秒强制刷新一次前台通知，避免部分 ROM 后台延迟更新通知栏
                        if (++pollCount % 15 == 0) {
                            postNotification(lastTitle.ifEmpty { "未在播放" }, lastSinger, lastPlaying)
                        }
                        session.setPlaybackState(
                            buildPlaybackState(
                                st.playing,
                                (st.position * 1000).toLong(),
                                if (st.playing) 1.0f else 0.0f
                            )
                        )
                    } else {
                        session.setPlaybackState(buildPlaybackState(false, 0, 0f))
                    }
                }
                try {
                    Thread.sleep(2000)
                } catch (_: InterruptedException) {
                    break
                }
            }
        }.start()
    }

    private fun buildMetadata(st: RemoteStatus): MediaMetadataCompat {
        val b = MediaMetadataCompat.Builder()
            .putString(MediaMetadataCompat.METADATA_KEY_TITLE, st.title.ifEmpty { "未在播放" })
            .putString(MediaMetadataCompat.METADATA_KEY_ARTIST, st.singer)
            .putString(MediaMetadataCompat.METADATA_KEY_ALBUM, st.source)
            .putLong(MediaMetadataCompat.METADATA_KEY_DURATION, (st.length * 1000).toLong())
        return b.build()
    }

    private fun buildPlaybackState(playing: Boolean, positionMs: Long, speed: Float): PlaybackStateCompat {
        val actions = PlaybackStateCompat.ACTION_PLAY or PlaybackStateCompat.ACTION_PAUSE or
            PlaybackStateCompat.ACTION_PLAY_PAUSE or PlaybackStateCompat.ACTION_SKIP_TO_NEXT or
            PlaybackStateCompat.ACTION_SKIP_TO_PREVIOUS or PlaybackStateCompat.ACTION_SEEK_TO or
            PlaybackStateCompat.ACTION_STOP
        return PlaybackStateCompat.Builder()
            .setActions(actions)
            .setState(if (playing) PlaybackStateCompat.STATE_PLAYING else PlaybackStateCompat.STATE_PAUSED, positionMs, speed)
            .build()
    }

    private fun postNotification(title: String, text: String, playing: Boolean) {
        val notif = buildNotification(title, text, playing)
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(NOTIF_ID, notif, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK)
        } else {
            startForeground(NOTIF_ID, notif)
        }
    }

    private fun buildNotification(title: String, text: String, playing: Boolean): Notification {
        val contentIntent = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val style = MediaStyle()
            .setMediaSession(session.sessionToken)
            .setShowActionsInCompactView(0, 1, 2)
        val builder = NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(contentIntent)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setCategory(NotificationCompat.CATEGORY_TRANSPORT)
            .setStyle(style)
            .addAction(android.R.drawable.ic_media_previous, "上一首", actionIntent(ACTION_PREV))
            .addAction(
                if (playing) android.R.drawable.ic_media_pause else android.R.drawable.ic_media_play,
                "播放/暂停",
                actionIntent(ACTION_PLAY)
            )
            .addAction(android.R.drawable.ic_media_next, "下一首", actionIntent(ACTION_NEXT))
            .addAction(R.drawable.ic_fav_notif, "收藏", actionIntent(ACTION_FAV))
        return builder.build()
    }

    private fun actionIntent(action: String): PendingIntent {
        val i = Intent(this, RemoteService::class.java).setAction(action)
        return PendingIntent.getService(
            this, action.hashCode(), i,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val ch = NotificationChannel(
                CHANNEL_ID, "播放控制", NotificationManager.IMPORTANCE_LOW
            ).apply {
                setShowBadge(false)
                lockscreenVisibility = Notification.VISIBILITY_PUBLIC
            }
            getSystemService(NotificationManager::class.java).createNotificationChannel(ch)
        }
    }

    override fun onDestroy() {
        polling = false
        handler.removeCallbacksAndMessages(null)
        if (::session.isInitialized) {
            session.isActive = false
            session.release()
        }
        super.onDestroy()
    }
}
