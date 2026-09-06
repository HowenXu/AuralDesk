package com.auraldesk.remote

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.content.SharedPreferences
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ImageButton
import android.widget.PopupMenu
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.TextView
import androidx.core.content.ContextCompat
import org.json.JSONArray
import org.json.JSONObject
import kotlin.concurrent.thread
import java.util.Locale

object RemoteLang {
    @Volatile var tag: String? = null   // zh/en；null = follow system
}

class MainActivity : Activity() {
    override fun attachBaseContext(newBase: Context) {
        val tag = RemoteLang.tag
        super.attachBaseContext(
            if (tag != null) {
                newBase.createConfigurationContext(
                    Configuration(newBase.resources.configuration).apply { setLocale(Locale.forLanguageTag(tag)) }
                )
            } else newBase
        )
    }
    private lateinit var rootPanel: LinearLayout
    private lateinit var connDot: View
    private lateinit var connText: TextView
    private lateinit var tabQq: Button
    private lateinit var tabQueue: Button
    private lateinit var qqPanel: LinearLayout
    private lateinit var queuePanel: LinearLayout
    private lateinit var qqBackBtn: Button
    private lateinit var qqTitle: TextView
    private lateinit var qqRandomBtn: Button
    private lateinit var qqSearchOpenBtn: ImageButton
    private lateinit var qqToolbar: LinearLayout
    private lateinit var qqSearchRow: LinearLayout
    private lateinit var qqListFilterRow: LinearLayout
    private lateinit var qqListFilter: EditText
    private lateinit var qqSortBtn: Button
    private lateinit var qqSearchInput: EditText
    private lateinit var qqSearchBtn: Button
    private lateinit var qqList: LinearLayout
    private lateinit var qqScroll: ScrollView
    private lateinit var queueList: LinearLayout
    private lateinit var queueScroll: ScrollView
    private lateinit var queueJumpBtn: Button
    private lateinit var queueSelectAllBtn: Button
    private lateinit var queueDeleteSelBtn: Button
    private lateinit var queueDoneBtn: Button
    private lateinit var nowTitle: TextView
    private lateinit var nowMeta: TextView
    private lateinit var seekBar: ProgressBar
    private lateinit var posText: TextView
    private lateinit var durText: TextView
    private lateinit var playBtn: ImageButton
    private lateinit var prevBtn: ImageButton
    private lateinit var nextBtn: ImageButton

    private var uiPolling = false
    private var userSeeking = false
    private var sig = ""
    private var lastCurrentIndex = 0
    private var multiSelect = false
    private val selected = mutableSetOf<Int>()
    private var lastQueueJson: JSONArray? = null
    private val phoneQueueExtra = mutableListOf<JSONObject>()
    private var queueLoadingMore = false
    private var lastMergedQueue: List<JSONObject> = emptyList()
    private lateinit var queueTitleText: TextView
    private lateinit var qqSearchPanel: LinearLayout
    private lateinit var qqSearchInput2: EditText
    private lateinit var qqSearchList: LinearLayout
    private lateinit var qqSearchTabSong: Button
    private lateinit var qqSearchTabAlbum: Button
    private lateinit var qqSearchTabSinger: Button
    private lateinit var singerPanel: LinearLayout
    private lateinit var singerTitle: TextView
    private lateinit var singerDesc: TextView
    private lateinit var singerTabSongs: Button
    private lateinit var singerTabAlbums: Button
    private lateinit var singerFilter: EditText
    private lateinit var singerList: LinearLayout
    private lateinit var albumListPanel: LinearLayout
    private lateinit var albumTitle: TextView
    private lateinit var albumList: LinearLayout
    private var searchTab = "song"
    private var singerTab = "songs"
    private var qqSortMode = "default"
    private var searchPanelLocal = false
    private var localBackView = "home"
    private var lastPlainView = "home"
    private var singerSongsAll = mutableListOf<JSONObject>()
    private var singerAlbumsAll = mutableListOf<JSONObject>()
    private var qqSongsAll = mutableListOf<JSONObject>()
    private lateinit var prefs: SharedPreferences

    private var bg = 0
    private var card = 0
    private var card2 = 0
    private var txt = 0
    private var dim = 0
    private var accent = 0
    private var sel = 0

    private fun dp(v: Int) =
        TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v.toFloat(), resources.displayMetrics).toInt()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = getSharedPreferences("auraldesk_remote", MODE_PRIVATE)
        val dark = (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
            Configuration.UI_MODE_NIGHT_YES
        if (dark) {
            bg = Color.rgb(16, 18, 22); card = Color.rgb(26, 29, 36); card2 = Color.rgb(33, 36, 44)
            txt = Color.rgb(232, 234, 237); dim = Color.rgb(154, 160, 168)
            accent = Color.rgb(68, 138, 220); sel = Color.rgb(14, 48, 80)
        } else {
            bg = Color.rgb(247, 247, 248); card = Color.WHITE; card2 = Color.rgb(238, 240, 243)
            txt = Color.rgb(27, 27, 31); dim = Color.rgb(107, 112, 119)
            accent = Color.rgb(10, 110, 190); sel = Color.rgb(214, 230, 245)
        }
        buildUi()
        ensureBatteryOptimization()
        requestNotifPermission()
        val saved = prefs.getString("baseUrl", "")
        if (!saved.isNullOrEmpty()) {
            RemoteClient.baseUrl = saved
            setConnConnecting()
            startPolling()
            startMediaService()
        } else {
            // 无保存地址：启动后自动扫描局域网连接一次
            thread {
                Thread.sleep(800)
                runOnUiThread { connect("") }
            }
        }
        checkForUpdatesWeekly()
    }

    /** Weekly silent update check at startup; no settings. On failure (GitHub unreachable) the check time is not recorded so it retries next launch. */
    private fun checkForUpdatesWeekly() {
        thread {
            try {
                val last = prefs.getLong("lastUpdateCheck", 0L)
                if (System.currentTimeMillis() - last < 7L * 86400_000L) return@thread
                val conn = java.net.URL("https://api.github.com/repos/HowenXu/AuralDesk/releases/latest")
                    .openConnection() as java.net.HttpURLConnection
                conn.connectTimeout = 8000
                conn.readTimeout = 8000
                conn.setRequestProperty("User-Agent", "AuralDeskRemote")
                if (conn.responseCode in 200..299) {
                    val j = JSONObject(conn.inputStream.bufferedReader().use { it.readText() })
                    val tag = j.optString("tag_name", "").trimStart('v')
                    val remote = parseVersion(tag)
                    val cur = parseVersion(packageManager.getPackageInfo(packageName, 0).versionName ?: "")
                    prefs.edit().putLong("lastUpdateCheck", System.currentTimeMillis()).apply()
                    if (tag.isNotEmpty() && remote != null && cur != null && greater(remote, cur)) {
                        runOnUiThread {
                            android.app.AlertDialog.Builder(this)
                                .setTitle(getString(R.string.update_title))
                                .setMessage(getString(R.string.update_msg, tag))
                                .setPositiveButton(android.R.string.yes) { _, _ ->
                                    startActivity(
                                        Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/HowenXu/AuralDesk/releases/latest"))
                                    )
                                }
                                .setNegativeButton(android.R.string.no, null)
                                .show()
                        }
                    }
                }
            } catch (_: Exception) {
                // offline / GitHub unreachable: lastUpdateCheck stays unset -> retried next start
            }
        }
    }

    private fun parseVersion(v: String): List<Int>? {
        val nums = ArrayList<Int>(3)
        for (s in v.trim().split('.').take(3)) {
            nums.add(s.toIntOrNull() ?: return null)
        }
        return nums
    }

    private fun greater(a: List<Int>, b: List<Int>): Boolean {
        for (i in 0 until minOf(a.size, b.size)) {
            if (a[i] != b[i]) return a[i] > b[i]
        }
        return a.size > b.size
    }

    private fun buildUi() {
        rootPanel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(bg)
        }

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), dp(16), dp(16), dp(8))
        }
        header.addView(TextView(this).apply {
            text = getString(R.string.remote_title)
            textSize = 17f
            setTextColor(txt)
            setTypeface(null, android.graphics.Typeface.BOLD)
        })
        header.addView(View(this).apply {
            layoutParams = LinearLayout.LayoutParams(0, 1, 1f)
        })
        connDot = View(this).apply {
            setBackgroundColor(Color.rgb(192, 57, 43))
            layoutParams = LinearLayout.LayoutParams(dp(9), dp(9))
        }
        header.addView(connDot)
        connText = TextView(this).apply {
            text = getString(R.string.status_disconnected)
            textSize = 12f
            setTextColor(dim)
            setPadding(dp(6), 0, 0, 0)
        }
        header.addView(connText)
        header.addView(Button(this).apply {
            text = getString(R.string.action_disconnect)
            textSize = 12f
            setTextColor(txt)
            setOnClickListener {
                stopService(Intent(this@MainActivity, RemoteService::class.java))
                setConn(false)
                sig = ""
                qqList.removeAllViews()
                queueList.removeAllViews()
            }
        })
        rootPanel.addView(header)

        val connRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), 0, dp(16), dp(8))
        }
        val addr = EditText(this).apply {
            hint = getString(R.string.addr_hint)
            textSize = 13f
            setTextColor(txt)
            setHintTextColor(dim)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        connRow.addView(addr)
        connRow.addView(Button(this).apply {
            text = getString(R.string.action_connect)
            setOnClickListener { connect(addr.text.toString().trim()) }
        })
        rootPanel.addView(connRow)

        val tabs = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(dp(16), 0, dp(16), dp(8))
        }
        tabQq = Button(this).apply { text = getString(R.string.tab_stream) }
        tabQueue = Button(this).apply { text = getString(R.string.tab_queue) }
        tabs.addView(tabQq, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        tabs.addView(tabQueue, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        tabQq.setOnClickListener { showTab("qq") }
        tabQueue.setOnClickListener { showTab("queue") }
        rootPanel.addView(tabs)

        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f
            )
        }

        qqPanel = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        qqToolbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), 0, dp(16), 0)
        }
        qqBackBtn = Button(this).apply { text = getString(R.string.back) }
        qqToolbar.addView(qqBackBtn)
        qqTitle = TextView(this).apply {
            textSize = 15f
            setTextColor(txt)
            setTypeface(null, android.graphics.Typeface.BOLD)
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(10), 0, dp(10), 0)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        qqToolbar.addView(qqTitle)
        qqRandomBtn = Button(this).apply { text = getString(R.string.shuffle) }
        qqToolbar.addView(qqRandomBtn)
        qqSearchOpenBtn = ImageButton(this).apply {
            setImageResource(R.drawable.ic_search)
            setBackgroundColor(Color.TRANSPARENT)
        }
        qqSearchOpenBtn.setOnClickListener { openSearchPanel() }
        qqToolbar.addView(qqSearchOpenBtn)
        qqBackBtn.visibility = View.GONE
        qqRandomBtn.visibility = View.GONE
        qqBackBtn.setOnClickListener { act(mapOf("action" to "back")) }
        qqRandomBtn.setOnClickListener { act(mapOf("action" to "random")) }
        qqPanel.addView(qqToolbar)

        qqSearchRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), dp(8), dp(16), 0)
            visibility = View.GONE
        }
        qqSearchInput = EditText(this).apply {
            hint = getString(R.string.search_placeholder)
            textSize = 13f
            setTextColor(txt)
            setHintTextColor(dim)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        qqSearchBtn = Button(this).apply { text = getString(R.string.search) }
        qqSearchRow.addView(qqSearchInput)
        qqSearchRow.addView(qqSearchBtn)
        qqSearchBtn.setOnClickListener {
            act(mapOf("action" to "search", "keyword" to qqSearchInput.text.toString()))
        }
        qqPanel.addView(qqSearchRow)

        // 歌曲列表本地过滤 + 排序行（仅歌曲视图显示）
        qqListFilterRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), dp(8), dp(16), 0)
            visibility = View.GONE
        }
        qqListFilter = EditText(this).apply {
            hint = getString(R.string.list_search_hint)
            textSize = 13f
            setTextColor(txt)
            setHintTextColor(dim)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        qqListFilter.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                applyQqListLocal()
            }
            override fun afterTextChanged(s: android.text.Editable?) {}
        })
        qqListFilterRow.addView(qqListFilter)
        qqSortBtn = Button(this).apply { text = getString(R.string.sort) }
        qqSortBtn.setOnClickListener { showQqSortMenu() }
        qqListFilterRow.addView(qqSortBtn)
        qqPanel.addView(qqListFilterRow)

        qqScroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        qqList = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(8), dp(16), dp(8))
        }
        qqScroll.addView(qqList)
        qqScroll.setOnScrollChangeListener { _, _, scrollY, _, _ ->
            val v = qqScroll.getChildAt(0)
            if (v != null && scrollY >= v.height - qqScroll.height) {
                act(mapOf("action" to "loadMore"))
            }
        }
        qqPanel.addView(qqScroll)

        // ============ 在线搜索面板 ============
        qqSearchPanel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            visibility = View.GONE
        }
        val sToolbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), 0, dp(16), 0)
        }
        sToolbar.addView(Button(this).apply {
            text = getString(R.string.back)
            setOnClickListener { panelBack() }
        })
        sToolbar.addView(TextView(this).apply {
            text = getString(R.string.online_search)
            textSize = 15f
            setTextColor(txt)
            setTypeface(null, android.graphics.Typeface.BOLD)
            setPadding(dp(10), 0, dp(10), 0)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        })
        qqSearchPanel.addView(sToolbar)
        val sRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), dp(8), dp(16), 0)
        }
        qqSearchInput2 = EditText(this).apply {
            hint = getString(R.string.online_hint)
            textSize = 13f
            setTextColor(txt)
            setHintTextColor(dim)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        sRow.addView(qqSearchInput2)
        sRow.addView(Button(this).apply {
            text = getString(R.string.search)
            setOnClickListener { doOnlineSearch() }
        })
        qqSearchPanel.addView(sRow)
        val sTabs = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(dp(16), dp(8), dp(16), 0)
        }
        qqSearchTabSong = Button(this).apply { text = getString(R.string.tab_songs) }
        qqSearchTabAlbum = Button(this).apply { text = getString(R.string.tab_albums) }
        qqSearchTabSinger = Button(this).apply { text = getString(R.string.tab_artists) }
        sTabs.addView(qqSearchTabSong, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        sTabs.addView(qqSearchTabAlbum, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        sTabs.addView(qqSearchTabSinger, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        qqSearchTabSong.setOnClickListener { act(mapOf("action" to "searchTab", "tab" to "song")) }
        qqSearchTabAlbum.setOnClickListener { act(mapOf("action" to "searchTab", "tab" to "album")) }
        qqSearchTabSinger.setOnClickListener { act(mapOf("action" to "searchTab", "tab" to "singer")) }
        qqSearchPanel.addView(sTabs)
        val sScroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        qqSearchList = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(8), dp(16), dp(8))
        }
        sScroll.addView(qqSearchList)
        qqSearchPanel.addView(sScroll)
        qqPanel.addView(qqSearchPanel, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))

        // ============ 歌手面板 ============
        singerPanel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            visibility = View.GONE
        }
        val siToolbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), 0, dp(16), 0)
        }
        siToolbar.addView(Button(this).apply {
            text = getString(R.string.back)
            setOnClickListener { panelBack() }
        })
        singerTitle = TextView(this).apply {
            textSize = 15f
            setTextColor(txt)
            setTypeface(null, android.graphics.Typeface.BOLD)
            setPadding(dp(10), 0, dp(10), 0)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        siToolbar.addView(singerTitle)
        singerPanel.addView(siToolbar)
        singerDesc = TextView(this).apply {
            textSize = 11f
            setTextColor(dim)
            setPadding(dp(16), dp(4), dp(16), 0)
        }
        singerPanel.addView(singerDesc)
        val siTabs = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(dp(16), dp(8), dp(16), 0)
        }
        singerTabSongs = Button(this).apply { text = getString(R.string.songs) }
        singerTabAlbums = Button(this).apply { text = getString(R.string.tab_albums) }
        siTabs.addView(singerTabSongs, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        siTabs.addView(singerTabAlbums, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        singerTabSongs.setOnClickListener { act(mapOf("action" to "singerTab", "tab" to "songs")) }
        singerTabAlbums.setOnClickListener { act(mapOf("action" to "singerTab", "tab" to "albums")) }
        singerPanel.addView(siTabs)
        singerFilter = EditText(this).apply {
            hint = getString(R.string.artist_search_hint)
            textSize = 13f
            setTextColor(txt)
            setHintTextColor(dim)
            setPadding(dp(16), dp(8), dp(16), 0)
        }
        singerFilter.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                applySingerLocalFilter()
            }
            override fun afterTextChanged(s: android.text.Editable?) {}
        })
        singerPanel.addView(singerFilter)
        val siScroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        singerList = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(8), dp(16), dp(8))
        }
        siScroll.addView(singerList)
        singerPanel.addView(siScroll)
        qqPanel.addView(singerPanel, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))

        // ============ 专辑列表面板（收藏专辑） ============
        albumListPanel = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            visibility = View.GONE
        }
        val alToolbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), 0, dp(16), 0)
        }
        alToolbar.addView(Button(this).apply {
            text = getString(R.string.back)
            setOnClickListener { panelBack() }
        })
        albumTitle = TextView(this).apply {
            text = getString(R.string.fav_albums)
            textSize = 15f
            setTextColor(txt)
            setTypeface(null, android.graphics.Typeface.BOLD)
            setPadding(dp(10), 0, dp(10), 0)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        alToolbar.addView(albumTitle)
        albumListPanel.addView(alToolbar)
        val alScroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        albumList = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(8), dp(16), dp(8))
        }
        alScroll.addView(albumList)
        albumListPanel.addView(alScroll)
        qqPanel.addView(albumListPanel, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))

        content.addView(qqPanel, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))

        queuePanel = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        val qToolbar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(16), 0, dp(16), 0)
        }
        queueTitleText = TextView(this).apply {
            text = getString(R.string.tab_queue)
            textSize = 15f
            setTextColor(txt)
            setTypeface(null, android.graphics.Typeface.BOLD)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        qToolbar.addView(queueTitleText)
        queueJumpBtn = Button(this).apply { text = getString(R.string.jump_current) }
        queueJumpBtn.setOnClickListener { jumpToCurrent() }
        queueSelectAllBtn = Button(this).apply { text = getString(R.string.select_all) }
        queueSelectAllBtn.setOnClickListener { toggleSelectAll() }
        queueDeleteSelBtn = Button(this).apply { text = getString(R.string.delete) }
        queueDeleteSelBtn.setOnClickListener { deleteSelected() }
        queueDoneBtn = Button(this).apply { text = getString(R.string.done) }
        queueDoneBtn.setOnClickListener { exitMultiSelect() }
        qToolbar.addView(queueJumpBtn)
        qToolbar.addView(queueSelectAllBtn)
        qToolbar.addView(queueDeleteSelBtn)
        qToolbar.addView(queueDoneBtn)
        queuePanel.addView(qToolbar)
        queueScroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        queueScroll.setOnScrollChangeListener { _, _, scrollY, _, _ ->
            val v = queueScroll.getChildAt(0)
            if (v != null && scrollY >= v.height - queueScroll.height) {
                loadMoreQueue()
            }
        }
        queueList = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(8), dp(16), dp(8))
        }
        queueScroll.addView(queueList)
        queuePanel.addView(queueScroll)
        content.addView(queuePanel, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))

        rootPanel.addView(content, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1f))

        val control = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(8), dp(16), dp(10))
            setBackgroundColor(card)
        }
        val metaRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        nowTitle = TextView(this).apply {
            textSize = 14f
            setTextColor(txt)
            setTypeface(null, android.graphics.Typeface.BOLD)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        nowMeta = TextView(this).apply {
            textSize = 11f
            setTextColor(dim)
        }
        metaRow.addView(nowTitle)
        metaRow.addView(nowMeta)
        control.addView(metaRow)

        // 进度条单独一行
        seekBar = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
            max = 1000
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(6))
        }
        control.addView(seekBar)
        // 时间行：左当前 / 右总时长，与进度条分开
        val timeRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(0, dp(4), 0, 0)
        }
        posText = TextView(this).apply { textSize = 10f; setTextColor(dim) }
        durText = TextView(this).apply { textSize = 10f; setTextColor(dim) }
        posText.text = "0:00"
        durText.text = "0:00"
        timeRow.addView(posText)
        timeRow.addView(View(this).apply {
            layoutParams = LinearLayout.LayoutParams(0, 1, 1f)
        })
        timeRow.addView(durText)
        control.addView(timeRow)

        val btnRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            setPadding(0, dp(4), 0, 0)
        }
        prevBtn = ImageButton(this).apply { setImageResource(android.R.drawable.ic_media_previous); setBackgroundColor(Color.TRANSPARENT) }
        playBtn = ImageButton(this).apply { setImageResource(android.R.drawable.ic_media_play); setBackgroundColor(Color.TRANSPARENT) }
        nextBtn = ImageButton(this).apply { setImageResource(android.R.drawable.ic_media_next); setBackgroundColor(Color.TRANSPARENT) }
        prevBtn.setOnClickListener { thread { RemoteClient.control("prev") } }
        playBtn.setOnClickListener { thread { RemoteClient.control("toggle") } }
        nextBtn.setOnClickListener { thread { RemoteClient.control("next") } }
        btnRow.addView(prevBtn, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        btnRow.addView(playBtn, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        btnRow.addView(nextBtn, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        control.addView(btnRow)
        rootPanel.addView(control)

        setContentView(rootPanel)
        showTab("qq")
    }

    private fun showTab(which: String) {
        val qq = which == "qq"
        qqPanel.visibility = if (qq) View.VISIBLE else View.GONE
        queuePanel.visibility = if (qq) View.GONE else View.VISIBLE
        tabQq.setBackgroundColor(if (qq) accent else card2)
        tabQueue.setBackgroundColor(if (qq) card2 else accent)
    }

    /** 系统返回键：面板→本地返回；流媒体非主页→回退一级；主页→退出应用。 */
    override fun onBackPressed() {
        if (queuePanel.visibility == View.VISIBLE) {
            showTab("qq")
            return
        }
        val panelOpen = searchPanelLocal ||
            qqSearchPanel.visibility == View.VISIBLE ||
            singerPanel.visibility == View.VISIBLE ||
            albumListPanel.visibility == View.VISIBLE
        if (panelOpen) {
            panelBack()
            return
        }
        if (lastPlainView != "home") {
            act(mapOf("action" to "back"))
            return
        }
        super.onBackPressed()
    }

    private fun setConn(on: Boolean) {
        connDot.setBackgroundColor(if (on) Color.rgb(29, 138, 62) else Color.rgb(192, 57, 43))
        connText.text = if (on) getString(R.string.connected) else getString(R.string.status_disconnected)
    }

    private fun setConnConnecting() {
        connDot.setBackgroundColor(Color.rgb(150, 150, 150))
        connText.text = getString(R.string.status_connecting)
    }

    private fun fmt(sec: Double): String {
        val s = if (sec.isNaN() || sec < 0) 0 else sec.toInt()
        return "${s / 60}:${String.format("%02d", s % 60)}"
    }

    private fun act(map: Map<String, Any?>) {
        thread { RemoteClient.qqAction(map) }
    }

    private fun connect(manual: String) {
        connText.text = getString(R.string.status_scanning)
        thread {
            var url: String? = if (manual.isEmpty()) RemoteClient.discover() else null
            if (url == null && manual.isNotEmpty()) {
                url = when {
                    manual.startsWith("http", true) -> manual
                    manual.contains(":") -> "http://$manual"
                    else -> "http://$manual:${RemoteClient.HTTP_PORT}"
                }
            }
            runOnUiThread {
                if (url == null) {
                    connText.text = getString(R.string.status_notfound)
                } else {
                    RemoteClient.baseUrl = url
                    prefs.edit().putString("baseUrl", url).apply()
                    setConnConnecting()
                    // 连接后让电脑端回到主页
                    act(mapOf("action" to "home"))
                    startPolling()
                    startMediaService()
                }
            }
        }
    }

    private fun startPolling() {
        if (uiPolling) return
        uiPolling = true
        thread {
            while (uiPolling) {
                val st = RemoteClient.qqState()
                runOnUiThread {
                    if (!uiPolling) return@runOnUiThread
                    if (st == null) {
                        setConn(false)
                        return@runOnUiThread
                    }
                    setConn(true)
                    render(st)
                }
                try {
                    Thread.sleep(500)
                } catch (_: InterruptedException) {
                    break
                }
            }
        }
    }

    private fun render(st: JSONObject) {
        val lg = st.optString("lang", "")
        if (lg.isNotEmpty() && RemoteLang.tag != lg) {
            RemoteLang.tag = lg
            recreate()
            return
        }
        lastCurrentIndex = st.optInt("currentIndex", lastCurrentIndex)
        val qt = st.optInt("queueTotal", 0)
        queueTitleText.text = if (qt > 0) getString(R.string.queue_count, qt) else getString(R.string.tab_queue)
        nowTitle.text = st.optString("curTitle", getString(R.string.not_playing))
        val singer = st.optString("curSinger", "")
        nowMeta.text = if (singer.isEmpty()) "" else " - $singer"
        val playing = st.optBoolean("playing", false)
        playBtn.setImageResource(if (playing) android.R.drawable.ic_media_pause else android.R.drawable.ic_media_play)
        val len = st.optDouble("length", 0.0)
        val pos = st.optDouble("position", 0.0)
        durText.text = fmt(len)
        seekBar.max = (len * 1000).toInt().coerceAtLeast(1)
        seekBar.progress = (pos * 1000).toInt().coerceIn(0, seekBar.max)
        posText.text = fmt(pos)

        val view = st.optString("view", "home")
        val title = st.optString("listTitle", "")
        val songSig = st.optJSONArray("songs")?.let { arr ->
            (0 until arr.length()).joinToString(",") {
                val o = arr.getJSONObject(it)
                val dl = o.optDouble("download", -1.0)
                o.optString("mid") + "|" + (if (dl < 0) "x" else (dl * 20).toInt().toString()) +
                    "|" + o.optBoolean("cached") + "|" + o.optBoolean("isFav")
            }
        } ?: ""
        val playlistSig = st.optJSONArray("playlists")?.let { arr ->
            (0 until arr.length()).joinToString(",") { arr.getJSONObject(it).optLong("id").toString() }
        } ?: ""
        val queueSig = st.optJSONArray("queue")?.let { arr ->
            (0 until arr.length()).joinToString(",") {
                val o = arr.getJSONObject(it)
                o.optString("title") + "|" + o.optBoolean("isCurrent") + "|" + o.optBoolean("isFav")
            }
        } ?: ""
        // 搜索/歌手/专辑数据也纳入签名：视图不变但结果更新时同样要重绘
        fun arrSig(arr: JSONArray?, key: String): String =
            arr?.let { a ->
                (0 until a.length()).joinToString(",") { a.getJSONObject(it).optString(key) }
            } ?: ""
        fun arrFavSig(arr: JSONArray?): String =
            arr?.let { a ->
                (0 until a.length()).joinToString(",") {
                    val o = a.getJSONObject(it)
                    o.optString("mid") + "|" + o.optBoolean("isFav")
                }
            } ?: ""
        val searchSongSig = arrFavSig(st.optJSONArray("searchSongs"))
        fun albumFavSig(arr: JSONArray?): String =
            arr?.let { a ->
                (0 until a.length()).joinToString(",") {
                    val o = a.getJSONObject(it)
                    o.optString("albumMid") + "|" + o.optBoolean("isFav")
                }
            } ?: ""
        val searchAlbumSig = albumFavSig(st.optJSONArray("searchAlbums"))
        val searchSingerSig = arrSig(st.optJSONArray("searchSingers"), "singerMid")
        val singerSongSig = arrFavSig(st.optJSONArray("singerSongs"))
        val singerAlbumSig = albumFavSig(st.optJSONArray("singerAlbums"))
        val favAlbumSig = albumFavSig(st.optJSONArray("favAlbums"))
        val tabSig = st.optString("searchTab", "") + "|" + st.optString("singerTab", "")
        val newSig = "$view|$title|$songSig|$playlistSig|$queueSig|$searchSongSig|$searchAlbumSig|$searchSingerSig|$singerSongSig|$singerAlbumSig|$favAlbumSig|$tabSig"
        if (newSig == sig) return
        sig = newSig

        qqTitle.text = title.ifEmpty { if (view == "home") getString(R.string.home) else getString(R.string.list_tab) }
        qqBackBtn.visibility = if (view == "home") View.GONE else View.VISIBLE
        qqRandomBtn.visibility = if (view == "songs") View.VISIBLE else View.GONE
        qqSearchRow.visibility = if (view == "songs") View.VISIBLE else View.GONE

        if (view == "searchOnline") searchPanelLocal = false
        if (searchPanelLocal) {
            showQqView("searchOnline")
        } else {
            if (view != "searchOnline" && view != "singer" && view != "albums")
                lastPlainView = view
            showQqView(view)
        }
        when (view) {
            "home" -> renderHome(st.optJSONArray("homeCards"))
            "playlists" -> renderPlaylists(st.optJSONArray("playlists"))
            "searchOnline" -> renderSearch(st)
            "singer" -> renderSinger(st)
            "albums" -> renderAlbums(st)
            else -> renderSongs(st.optJSONArray("songs"))
        }
        renderQueue(st.optJSONArray("queue"))
    }

    /** 切换流媒体主区域：普通列表 vs 搜索/歌手/专辑面板。 */
    private fun showQqView(view: String) {
        val simple = view == "searchOnline" || view == "singer" || view == "albums"
        qqToolbar.visibility = if (simple) View.GONE else View.VISIBLE
        qqScroll.visibility = if (simple) View.GONE else View.VISIBLE
        qqListFilterRow.visibility = if (view == "songs") View.VISIBLE else View.GONE
        qqSearchPanel.visibility = if (view == "searchOnline") View.VISIBLE else View.GONE
        singerPanel.visibility = if (view == "singer") View.VISIBLE else View.GONE
        albumListPanel.visibility = if (view == "albums") View.VISIBLE else View.GONE
    }

    /** 手机端本地打开搜索面板（先输入关键词，再发在线搜索）。 */
    private fun openSearchPanel() {
        searchPanelLocal = true
        localBackView = lastPlainView
        showQqView("searchOnline")
        qqSearchInput2.requestFocus()
    }

    /** 面板返回：本地立即切回上一个视图，同时通知电脑端同步。 */
    private fun panelBack() {
        searchPanelLocal = false
        showQqView(localBackView)
        act(mapOf("action" to "back"))
    }

    private fun doOnlineSearch() {
        val kw = qqSearchInput2.text.toString().trim()
        if (kw.isEmpty()) return
        act(mapOf("action" to "searchOnline", "keyword" to kw))
    }

    private fun emptyHint(text: String): TextView = TextView(this).apply {
        this.text = text
        textSize = 13f
        setTextColor(dim)
        setPadding(0, dp(20), 0, 0)
    }

    private fun renderSongItems(arr: JSONArray?, container: LinearLayout) {
        container.removeAllViews()
        if (arr == null || arr.length() == 0) {
            container.addView(emptyHint(getString(R.string.empty_songs)))
            return
        }
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            container.addView(songRow(o))
        }
    }

    private fun renderAlbumItems(arr: JSONArray?, container: LinearLayout) {
        container.removeAllViews()
        if (arr == null || arr.length() == 0) {
            container.addView(emptyHint(getString(R.string.empty_albums)))
            return
        }
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            container.addView(albumCardRow(o) {
                act(mapOf(
                    "action" to "openAlbum",
                    "albumMid" to o.optString("albumMid"),
                    "name" to o.optString("name")
                ))
            })
        }
    }

    /** 专辑行：专辑名 + 作者·日期（不加载/显示封面，省带宽）。 */
    private fun albumCardRow(o: JSONObject, onClick: () -> Unit): View {
        val name = o.optString("name", "")
        val singer = o.optString("singer", "")
        val date = o.optString("date", "")
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(12), dp(8), dp(12), dp(8))
            setBackgroundColor(card)
            val lp = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT)
            lp.setMargins(0, 0, 0, dp(6))
            layoutParams = lp
            setOnClickListener { onClick() }
        }
        val textCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        textCol.addView(TextView(this).apply {
            text = name
            textSize = 15f
            setTextColor(txt)
        })
        textCol.addView(TextView(this).apply {
            text = listOf(singer, date).filter { it.isNotEmpty() }.joinToString(" · ")
            textSize = 11f
            setTextColor(dim)
            setPadding(0, dp(2), 0, 0)
        })
        row.addView(textCol)
        val albumMid = o.optString("albumMid", "")
        val albumFav = o.optBoolean("isFav", false)
        row.addView(ImageView(this).apply {
            layoutParams = LinearLayout.LayoutParams(dp(26), dp(26)).apply { marginStart = dp(8) }
            scaleType = ImageView.ScaleType.CENTER_INSIDE
            setImageResource(if (albumFav) R.drawable.ic_heart_filled else R.drawable.ic_heart_outline)
            setColorFilter(if (albumFav) Color.rgb(224, 36, 94) else dim)
            contentDescription = if (albumFav) getString(R.string.fav_album_on) else getString(R.string.fav_album_off)
            setOnClickListener { act(mapOf("action" to "toggleFavAlbum", "albumMid" to albumMid)) }
        })
        return row
    }

    private fun renderSingerItems(arr: JSONArray?, container: LinearLayout) {
        container.removeAllViews()
        if (arr == null || arr.length() == 0) {
            container.addView(emptyHint(getString(R.string.empty_artists)))
            return
        }
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            val sub = getString(R.string.song_album_line, o.optInt("songNum", 0), o.optInt("albumNum", 0))
            container.addView(cardRow(o.optString("name", ""), sub) {
                act(mapOf(
                    "action" to "openSinger",
                    "singerMid" to o.optString("singerMid"),
                    "name" to o.optString("name")
                ))
            })
        }
    }

    private fun renderSearch(st: JSONObject) {
        searchTab = st.optString("searchTab", searchTab)
        qqSearchTabSong.setBackgroundColor(if (searchTab == "song") accent else card2)
        qqSearchTabAlbum.setBackgroundColor(if (searchTab == "album") accent else card2)
        qqSearchTabSinger.setBackgroundColor(if (searchTab == "singer") accent else card2)
        when (searchTab) {
            "album" -> renderAlbumItems(st.optJSONArray("searchAlbums"), qqSearchList)
            "singer" -> renderSingerItems(st.optJSONArray("searchSingers"), qqSearchList)
            else -> renderSongItems(st.optJSONArray("searchSongs"), qqSearchList)
        }
    }

    private fun renderSinger(st: JSONObject) {
        localBackView = "searchOnline" // 歌手页来自搜索，返回=搜索面板
        singerTitle.text = st.optString("singerName", getString(R.string.artist))
        singerDesc.text = st.optString("singerDesc", "")
        singerTab = st.optString("singerTab", "songs")
        singerTabSongs.setBackgroundColor(if (singerTab == "songs") accent else card2)
        singerTabAlbums.setBackgroundColor(if (singerTab == "albums") accent else card2)
        singerSongsAll.clear()
        st.optJSONArray("singerSongs")?.let { arr ->
            for (i in 0 until arr.length()) singerSongsAll.add(arr.getJSONObject(i))
        }
        singerAlbumsAll.clear()
        st.optJSONArray("singerAlbums")?.let { arr ->
            for (i in 0 until arr.length()) singerAlbumsAll.add(arr.getJSONObject(i))
        }
        applySingerLocalFilter()
    }

    private fun applySingerLocalFilter() {
        if (!::singerList.isInitialized) return
        val kw = singerFilter.text.toString().trim()
        singerList.removeAllViews()
        if (singerTab == "albums") {
            val list = singerAlbumsAll.filter {
                kw.isEmpty() || it.optString("name").contains(kw, true) || it.optString("date").contains(kw, true)
            }
            if (list.isEmpty()) {
                singerList.addView(emptyHint("暂无专辑"))
                return
            }
            for (o in list) {
                singerList.addView(albumCardRow(o) {
                    act(mapOf(
                        "action" to "openAlbum",
                        "albumMid" to o.optString("albumMid"),
                        "name" to o.optString("name")
                    ))
                })
            }
        } else {
            val list = singerSongsAll.filter {
                kw.isEmpty() || it.optString("title").contains(kw, true) || it.optString("singer").contains(kw, true)
            }
            if (list.isEmpty()) {
                singerList.addView(emptyHint("暂无歌曲"))
                return
            }
            for (o in list) {
                singerList.addView(songRow(o))
            }
        }
    }

    private fun renderAlbums(st: JSONObject) {
        localBackView = "home" // 收藏专辑来自主页，返回=主页
        albumTitle.text = getString(R.string.fav_albums)
        renderAlbumItems(st.optJSONArray("favAlbums"), albumList)
    }

    private fun cardRow(title: String, sub: String, onClick: (() -> Unit)?): Button {
        val text2 = if (sub.isEmpty()) title else "$title\n$sub"
        return Button(this).apply {
            text = text2
            textSize = 14f
            setTextColor(txt)
            gravity = Gravity.CENTER_VERTICAL or Gravity.START
            setPadding(dp(14), dp(8), dp(14), dp(8))
            setBackgroundColor(card)
            val lp = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT)
            lp.setMargins(0, 0, 0, dp(8))
            layoutParams = lp
            setOnClickListener { onClick?.invoke() }
        }
    }

    private fun renderHome(cards: JSONArray?) {
        qqList.removeAllViews()
        if (cards == null || cards.length() == 0) {
            qqList.addView(TextView(this).apply {
                text = getString(R.string.need_login)
                textSize = 13f
                setTextColor(dim)
                setPadding(0, dp(20), 0, 0)
            })
            return
        }
        for (i in 0 until cards.length()) {
            val c = cards.getJSONObject(i)
            val type = c.optString("type", "")
            qqList.addView(
                cardRow(c.optString("title", ""), c.optString("sub", "")) {
                    act(
                        mapOf(
                                "action" to when (type) {
                                    "infinite" -> "radar"
                                    "daily30" -> "daily30"
                                    "fav" -> "fav"
                                    "favalbums" -> "favAlbums"
                                    else -> "playlists"
                                }
                        )
                    )
                }
            )
        }
    }

    private fun renderPlaylists(list: JSONArray?) {
        qqList.removeAllViews()
        if (list == null || list.length() == 0) {
            qqList.addView(TextView(this).apply {
                text = getString(R.string.no_playlists)
                textSize = 13f
                setTextColor(dim)
                setPadding(0, dp(20), 0, 0)
            })
            return
        }
        for (i in 0 until list.length()) {
            val p = list.getJSONObject(i)
            val id = p.optLong("id")
            qqList.addView(
                cardRow(p.optString("name", ""), p.optString("info", "")) {
                    act(mapOf("action" to "openPlaylist", "id" to id.toString(), "name" to p.optString("name", "")))
                }
            )
        }
    }

    private fun renderSongs(list: JSONArray?) {
        qqSongsAll.clear()
        if (list != null) {
            for (i in 0 until list.length()) qqSongsAll.add(list.getJSONObject(i))
        }
        applyQqListLocal()
    }

    /** 应用歌曲列表的本地过滤与排序后重绘。 */
    private fun applyQqListLocal() {
        if (!::qqList.isInitialized) return
        qqList.removeAllViews()
        var items = qqSongsAll.toMutableList()
        val kw = qqListFilter.text.toString().trim()
        if (kw.isNotEmpty()) {
            items = items.filter {
                it.optString("title").contains(kw, true) || it.optString("singer").contains(kw, true)
            }.toMutableList()
        }
        when (qqSortMode) {
            "title" -> items.sortBy { it.optString("title") }
            "singer" -> items.sortBy { it.optString("singer") }
            "duration" -> items.sortBy { parseDur(it.optString("duration")) }
            "album" -> items.sortBy { it.optString("album") }
        }
        if (items.isEmpty()) {
            qqList.addView(emptyHint("暂无歌曲"))
            return
        }
        for (s in items) qqList.addView(renderSongRow(s))
    }

    /** 渲染单行歌曲卡片。 */
    private fun renderSongRow(s: JSONObject): View {
        val download = s.optDouble("download", -1.0)
        val cached = s.optBoolean("cached", false)
        val badge = when {
            download >= 0.0 -> getString(R.string.downloading, (download * 100).toInt())
            cached -> getString(R.string.cached)
            else -> null
        }
        return songRow(s, badge)
    }

    /** 渲染单行歌曲卡片（标题/歌手 + 红心收藏按钮，点击红心不触发行播放）。 */
    private fun songRow(s: JSONObject, badge: String? = null): View {
        val mid = s.optString("mid", "")
        val singer = s.optString("singer", "")
        val fav = s.optBoolean("isFav", false)
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(14), dp(9), dp(14), dp(9))
            setBackgroundColor(card)
            val lp = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT)
            lp.setMargins(0, 0, 0, dp(6))
            layoutParams = lp
            setOnClickListener { act(mapOf("action" to "play", "mid" to mid)) }
        }
        val textCol = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        val line1 = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        line1.addView(TextView(this).apply {
            text = s.optString("title", "")
            textSize = 16f
            setTextColor(txt)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        })
        if (badge != null) {
            line1.addView(TextView(this).apply {
                text = badge
                textSize = 10f
                setTextColor(if (s.optBoolean("cached", false)) Color.rgb(29, 138, 62) else accent)
                setPadding(dp(6), 0, 0, 0)
            })
        }
        textCol.addView(line1)
        val small = listOf(singer, s.optString("duration")).filter { it.isNotEmpty() }.joinToString(" · ")
        if (small.isNotEmpty()) {
            textCol.addView(TextView(this).apply {
                text = small
                textSize = 12f
                setTextColor(dim)
                setPadding(0, dp(3), 0, 0)
            })
        }
        row.addView(textCol)
        row.addView(ImageView(this).apply {
            layoutParams = LinearLayout.LayoutParams(dp(26), dp(26)).apply { marginStart = dp(8) }
            scaleType = ImageView.ScaleType.CENTER_INSIDE
            setImageResource(if (fav) R.drawable.ic_heart_filled else R.drawable.ic_heart_outline)
            setColorFilter(if (fav) Color.rgb(224, 36, 94) else dim)
            contentDescription = if (fav) getString(R.string.fav_on) else getString(R.string.fav_off)
            setOnClickListener { act(mapOf("action" to "toggleFav", "mid" to mid)) }
        })
        return row
    }

    private fun parseDur(d: String): Int {
        val m = Regex("(\\d+):(\\d+)").find(d) ?: return 0
        return m.groupValues[1].toInt() * 60 + m.groupValues[2].toInt()
    }

    private fun showQqSortMenu() {
        val pm = PopupMenu(this, qqSortBtn)
        val items = listOf(
            getString(R.string.sort_default) to "default",
            getString(R.string.col_title) to "title",
            getString(R.string.col_singer) to "singer",
            getString(R.string.col_duration) to "duration",
            getString(R.string.col_album) to "album")
        items.forEach { (label, _) -> pm.menu.add(label) }
        pm.setOnMenuItemClickListener { item ->
            val hit = items.firstOrNull { it.first == item.title.toString() }
            if (hit != null) {
                qqSortMode = hit.second
                qqSortBtn.text = item.title.toString()
                applyQqListLocal()
            }
            true
        }
        pm.show()
    }

    private fun renderQueue(list: JSONArray?) {
        lastQueueJson = list
        queueJumpBtn.visibility = if (multiSelect) View.GONE else View.VISIBLE
        queueSelectAllBtn.visibility = if (multiSelect) View.VISIBLE else View.GONE
        queueDeleteSelBtn.visibility = if (multiSelect) View.VISIBLE else View.GONE
        queueDoneBtn.visibility = if (multiSelect) View.VISIBLE else View.GONE
        queueList.removeAllViews()
        // 合并分页数据：轮询前 30 + 手动加载的后续页，按电脑端原始索引排序
        val merged = mutableListOf<JSONObject>()
        if (list != null) {
            for (i in 0 until list.length()) merged.add(list.getJSONObject(i))
        }
        for (extra in phoneQueueExtra) merged.add(extra)
        merged.sortBy { it.optInt("idx") }
        lastMergedQueue = merged
        if (merged.isEmpty()) {
            queueList.addView(TextView(this).apply {
                text = getString(R.string.queue_empty)
                textSize = 13f
                setTextColor(dim)
                setPadding(0, dp(20), 0, 0)
            })
            return
        }
        for (i in 0 until merged.size) {
            val q = merged[i]
            val current = q.optBoolean("isCurrent", false)
            if (current) lastCurrentIndex = i
            val cached = q.optBoolean("cached", false) || q.optString("cache", "").isNotEmpty()
            val qmid = q.optString("mid", "")
            val idx = q.optInt("idx")
            val isSel = selected.contains(idx)
            val line = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setPadding(dp(12), dp(8), dp(12), dp(8))
                setBackgroundColor(when {
                    isSel -> sel
                    current -> card2
                    else -> card
                })
                val lp = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT)
                lp.setMargins(0, 0, 0, dp(6))
                layoutParams = lp
            }
            val mark = View(this).apply {
                setBackgroundColor(if (current && !multiSelect) accent else if (isSel) accent else Color.TRANSPARENT)
                layoutParams = LinearLayout.LayoutParams(dp(4), dp(20))
            }
            line.addView(mark)
            line.addView(LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
                setPadding(dp(8), 0, dp(8), 0)
                addView(LinearLayout(this@MainActivity).apply {
                    orientation = LinearLayout.HORIZONTAL
                    gravity = Gravity.CENTER_VERTICAL
                    addView(TextView(this@MainActivity).apply {
                        text = (if (isSel) "☑ " else "") + q.optString("title", "")
                        textSize = 14f
                        setTextColor(if (current) accent else txt)
                        layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
                    })
                    if (cached && !isSel) {
                        addView(TextView(this@MainActivity).apply {
                            text = getString(R.string.cached)
                            textSize = 10f
                            setTextColor(Color.rgb(29, 138, 62))
                            setPadding(dp(6), 0, 0, 0)
                        })
                    }
                })
                addView(TextView(this@MainActivity).apply {
                    text = q.optString("singer", "")
                    textSize = 11f
                    setTextColor(dim)
                    setPadding(0, dp(2), 0, 0)
                })
            })
            if (qmid.isNotEmpty() && !multiSelect) {
                val qFav = q.optBoolean("isFav", false)
                line.addView(ImageView(this@MainActivity).apply {
                    layoutParams = LinearLayout.LayoutParams(dp(24), dp(24)).apply { marginStart = dp(4) }
                    scaleType = ImageView.ScaleType.CENTER_INSIDE
                    setImageResource(if (qFav) R.drawable.ic_heart_filled else R.drawable.ic_heart_outline)
                    setColorFilter(if (qFav) Color.rgb(224, 36, 94) else dim)
                    contentDescription = if (qFav) getString(R.string.fav_on) else getString(R.string.fav_off)
                    setOnClickListener { act(mapOf("action" to "toggleFav", "mid" to qmid)) }
                })
            }
            if (!multiSelect) {
                val more = Button(this).apply { text = "⋮" }
                more.setOnClickListener { v ->
                    val pm = PopupMenu(this@MainActivity, v)
                    val items = listOf(
                        getString(R.string.move_up) to "queueUp",
                        getString(R.string.move_down) to "queueDown",
                        getString(R.string.delete) to "queueDelete")
                    items.forEach { (label, _) -> pm.menu.add(label) }
                    pm.setOnMenuItemClickListener { item ->
                        val hit = items.firstOrNull { it.first == item.title.toString() }
                        if (hit != null)
                            act(mapOf("action" to hit.second, "index" to idx.toString()))
                        true
                    }
                    pm.show()
                }
                line.addView(more)
                line.setOnClickListener { act(mapOf("action" to "queuePlay", "index" to idx.toString())) }
            } else {
                line.setOnClickListener {
                    if (!selected.remove(idx)) selected.add(idx)
                    refreshQueue()
                }
            }
            line.setOnLongClickListener {
                enterMultiSelect(idx)
                true
            }
            queueList.addView(line)
        }
    }

    private fun enterMultiSelect(idx: Int) {
        if (!multiSelect) {
            multiSelect = true
            selected.clear()
        }
        if (!selected.remove(idx)) selected.add(idx)
        refreshQueue()
    }

    private fun exitMultiSelect() {
        multiSelect = false
        selected.clear()
        refreshQueue()
    }

    private fun toggleSelectAll() {
        val all = lastMergedQueue.map { it.optInt("idx") }
        if (all.isNotEmpty() && selected.containsAll(all)) {
            selected.clear()
        } else {
            selected.clear()
            selected.addAll(all)
        }
        refreshQueue()
    }

    private fun deleteSelected() {
        // 从大到小删除，避免索引错位
        val indices = selected.sortedDescending()
        for (idx in indices) {
            act(mapOf("action" to "queueDelete", "index" to idx.toString()))
        }
        exitMultiSelect()
    }

    private fun refreshQueue() {
        renderQueue(lastQueueJson)
    }

    private fun loadMoreQueue() {
        if (queueLoadingMore) return
        val start = 30 + phoneQueueExtra.size
        queueLoadingMore = true
        thread {
            val st = RemoteClient.qqState(start, 30)
            runOnUiThread {
                queueLoadingMore = false
                if (st != null) {
                    val arr = st.optJSONArray("queue")
                    if (arr != null && arr.length() > 0) {
                        for (i in 0 until arr.length()) phoneQueueExtra.add(arr.getJSONObject(i))
                        refreshQueue()
                    }
                }
            }
        }
    }

    /** 跳到当前播放歌曲：已在列表内直接滚动；在已加载之外先拉取所在页再滚动。 */
    private fun jumpToCurrent() {
        val idx = lastCurrentIndex
        if (idx < 0) return
        val loadedPos = lastMergedQueue.indexOfFirst { it.optInt("idx") == idx }
        if (loadedPos >= 0) {
            val v = queueList.getChildAt(loadedPos)
            if (v != null) queueScroll.smoothScrollTo(0, v.top)
            return
        }
        // 当前歌在已加载的 30 首之外：请求它所在分页并合并显示
        val qstart = (idx / 30) * 30
        thread {
            val st = RemoteClient.qqState(qstart, 30)
            runOnUiThread {
                val arr = st?.optJSONArray("queue") ?: return@runOnUiThread
                for (i in 0 until arr.length()) {
                    val o = arr.getJSONObject(i)
                    if (phoneQueueExtra.none { it.optInt("idx") == o.optInt("idx") })
                        phoneQueueExtra.add(o)
                }
                refreshQueue()
                val pos = lastMergedQueue.indexOfFirst { it.optInt("idx") == idx }
                if (pos >= 0) {
                    val v = queueList.getChildAt(pos)
                    if (v != null) queueScroll.smoothScrollTo(0, v.top)
                }
            }
        }
    }

    /** 启动时检查电池优化白名单，未豁免则申请（系统弹窗）。 */
    private fun ensureBatteryOptimization() {
        try {
            val pm = getSystemService(PowerManager::class.java)
            if (!pm.isIgnoringBatteryOptimizations(packageName)) {
                startActivity(
                    Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS)
                        .setData(Uri.parse("package:$packageName"))
                )
            }
        } catch (_: Exception) {
            openAppDetails()
        }
    }

    private fun openAppDetailsIntent(): Intent =
        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).setData(Uri.parse("package:$packageName"))

    private fun openAppDetails() {
        try {
            startActivity(openAppDetailsIntent())
        } catch (_: Exception) { }
    }

    /** 启动媒体控制前台服务，让系统控制中心出现播放卡片。 */
    private fun startMediaService() {
        val url = RemoteClient.baseUrl
        if (url.isEmpty()) return
        try {
            val i = Intent(this, RemoteService::class.java)
                .setAction(RemoteService.ACTION_CONNECT)
                .putExtra(RemoteService.EXTRA_URL, url)
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(i) else startService(i)
        } catch (_: Exception) { }
    }

    private fun requestNotifPermission() {
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1001)
        }
    }

    override fun onStart() {
        super.onStart()
        if (RemoteClient.baseUrl.isNotEmpty()) {
            startPolling()
            // 回到前台时让媒体服务立即刷新一次通知（后台期间可能被系统延迟）
            try {
                val i = Intent(this, RemoteService::class.java)
                    .setAction(RemoteService.ACTION_REFRESH)
                    .putExtra(RemoteService.EXTRA_URL, RemoteClient.baseUrl)
                startService(i)
            } catch (_: Exception) { }
        }
    }

    override fun onStop() {
        super.onStop()
        stopPolling()
    }

    override fun onDestroy() {
        stopPolling()
        super.onDestroy()
    }

    private fun stopPolling() {
        uiPolling = false
    }
}
