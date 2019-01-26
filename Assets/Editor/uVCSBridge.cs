/*
 * Copyright (c) 2016 DandyMania
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 *
 * Latest version: https://github.com/DandyMania/uVCSBridge.git
*/


using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;


public class uVCSBridge : MonoBehaviour
{

    ///---------------------------------------------------------------------
    /// プリファレンス
    ///---------------------------------------------------------------------

    /// <summary>
    /// VCSのタイプ
    /// </summary>
    public enum VCSType
    {
        SVN = 0,
        GIT,
        HG,  // Mercurial
    }

    /// <summary>
    /// 初期設定
    /// </summary>
    public class Defaults
    {
        public const bool IconOverlay = true;
        public const bool IconOverlayOnlyModified = false;
        static public VCSType VcsType = VCSType.SVN;
    }
    /// <summary>
    /// 設定パラメータ
    /// </summary>
    static public class Settings
    {
        static public bool IconOverlay = true;              // アイコンオーバーレイ
        static public bool IconOverlayOnlyModified = false; // 変更された人だけ表示
        static public VCSType VcsType = VCSType.SVN;
    }

    /// <summary>
    /// プリファレンスセーブ/ロード ラッパー
    /// /// <summary>
    public static BoolSetting BoolSet = new BoolSetting();
    public class BoolSetting
    {
        public bool this[string pName, bool pDefault = false]
        {
            get { return (EditorPrefs.GetBool("UVCS_"+ "_" + pName, pDefault)); }
            set { EditorPrefs.SetBool("UVCS_" +"_" + pName, value); }
        }
    }
    public static IntSetting IntSet = new IntSetting();
    public class IntSetting
    {
        public int this[string pName, int pDefault = 0]
        {
            get { return (EditorPrefs.GetInt("UVCS_" + "_" + pName, pDefault)); }
            set { EditorPrefs.SetInt("UVCS_"  + "_" + pName, value); }
        }
    }

    // プロジェクトウィンドウ取得
    public static EditorWindow GetWindowByName(string pName)
    {
        UnityEngine.Object[] objectList = Resources.FindObjectsOfTypeAll(typeof(EditorWindow));

        foreach (UnityEngine.Object obj in objectList)
        {
            if (obj.GetType().ToString() == pName)
                return ((EditorWindow)obj);
        }

        return (null);
    }
    private static EditorWindow _projectWindow = null;
    public static EditorWindow ProjectWindow
    {
        get
        {
            _projectWindow = _projectWindow
                          ?? GetWindowByName("UnityEditor.ProjectWindow")
                          ?? GetWindowByName("UnityEditor.ObjectBrowser")
                          ?? GetWindowByName("UnityEditor.ProjectBrowser");

            return (_projectWindow);
        }
    }

    /// <summary>
    /// プリファレンス画面
    /// </summary>
    [PreferenceItem("uVCSBridge")]
    public static void DrawPreferences()
    {
        // create a single preferences tab to manage all the enhancement's preferences

        //EditorGUILayout.BeginHorizontal();


        // VCSの種類
        VCSType newVcs = (VCSType)EditorGUILayout.EnumPopup("VCS Type", Settings.VcsType);
        if (newVcs != Settings.VcsType)
        {
            Settings.VcsType = newVcs;
        }

        // オーバーレイアイコン
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Enable Status Overlay Icon" + "  ");
        {
            bool newEnabled = EditorGUILayout.Toggle(Settings.IconOverlay);
            if (newEnabled != Settings.IconOverlay)
            {
                Settings.IconOverlay = newEnabled;
            }
         }
        EditorGUILayout.EndHorizontal();

        // オーバーレイアイコン(変更された人だけ表示)
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Status Overlay Icon Only Modified" + "  ");
        {
            bool newEnabled = EditorGUILayout.Toggle(Settings.IconOverlayOnlyModified);
            if (newEnabled != Settings.IconOverlayOnlyModified)
            {
                Settings.IconOverlayOnlyModified = newEnabled;
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUI.changed)
        {
            SaveSettings();
            Reset();
            // ステータス更新
            VCSStatusUpdate(ASSET_ROOT_DIR);
            
        }
    }


    /// <summary>
    /// 設定ロード
    /// </summary>
    private static void ReadSettings()
    {
        Settings.IconOverlay             = BoolSet["IconOverlay", Defaults.IconOverlay];
        Settings.IconOverlayOnlyModified = BoolSet["IconOverlayOnlyModified", Defaults.IconOverlayOnlyModified];
        Settings.VcsType = (VCSType)IntSet["VcsType", (int)Defaults.VcsType];

    }

    /// <summary>
    /// 設定セーブ
    /// </summary>
    private static void SaveSettings()
    {
        BoolSet[ "IconOverlay"] = Settings.IconOverlay;
        BoolSet[ "IconOverlayOnlyModified"] = Settings.IconOverlayOnlyModified;
        IntSet[ "VcsType"] = (int)Settings.VcsType;
    }




    ///---------------------------------------------------------------------
    /// VCS連携処理
    ///---------------------------------------------------------------------


    // 実行ファイル
    static string[] tortoiseProc = { "TortoiseProc", "TortoiseGitProc", "thg" };
    static string[] consoleExe = { "svn", "git", "hg" };
    static string[] consoleCmd = { "-v wc", "-v -u -s", "-A"};
    static string[] consoleCmdDir = { "wc", "-v -u -s", ""};


	// 選択中のオブジェクトが存在するかどうかを返します
	private const string ASSET_ROOT = "Assets";

	private const string ASSET_ROOT_DIR = ASSET_ROOT + "/";


	/*
	// 選択中のオブジェクトの Assets フォルダからの相対パスを取得します
	private static string SelectAssetPath
	{
		get
		{
			// ファイルが選択されている時.
			if (Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0)
			{
				return AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
			}
			return ASSET_ROOT_DIR;
		}
	}
	*/

	// 選択中のオブジェクトの Assets フォルダからの相対パスを取得します
	// 複数選択対応
	private static List<string> SelectAssetPaths
	{
		get
		{
			var selectFileList = new List<string>();
			// ファイルが選択されている時.
			if (Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0)
			{
				foreach (var file in Selection.assetGUIDs)
				{
					selectFileList.Add(AssetDatabase.GUIDToAssetPath(file));
				}
			}

			if (selectFileList.Count == 0)
			{
				selectFileList.Add(ASSET_ROOT_DIR);
			}


			return selectFileList;
		}
	}

	private static bool IsSelectAsset
	{
		get
		{
			return Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0;
		}
	}


	/// <summary>
	/// プロジェクトビューで選択中のファイルのフルパス取得
	/// </summary>
	/// <returns></returns>
	private static List<string> getFullPaths()
	{

		var selectFileList = new List<string>();

		foreach (var file in SelectAssetPaths)
		{
			var startIndex = file.IndexOf(ASSET_ROOT);
			var assetPath = file.Remove(startIndex, ASSET_ROOT.Length);
			selectFileList.Add(Application.dataPath + assetPath);
		}

		return selectFileList;
	}


	/// <summary>
	/// プロジェクトビューにSVNの状態表示
	/// </summary>
	[InitializeOnLoadMethod]
	private static void Bridge()
	{
		EditorApplication.projectWindowItemOnGUI += OnGUI;

        // 設定ロード
        ReadSettings();

		// SVNステータス更新
		VCSStatusUpdate(ASSET_ROOT_DIR);
	}

    // 設定切り替え後の状態リセット
    private static void Reset()
    {
        FolderStatusMap.Clear();
        FileStatusMap.Clear();
        svnupdatepath = "";
    }



	enum VCSStatus
	{
        NORMAL = 0, // 変更なし
		UNMANAGE,   // 追加されてない
		EDIT,       // 変更有
		ADD,        // 追加
		CONFRICT,   // コンフリクト
		DELETE,     // 削除
	};
    static string[] StatusString = { "-", "?", "M", "A", "C", "D" };
    static string[] StatusStringHG = { "C", "?", "M", "A", "C", "R" };

    static string[] StatusIconString = { "V", "?", "!", "+", "*", "x" };

    static Color[] StatusIconColor = {  new Color(0, 1, 0, 0.5f), new Color(0.5f, 0.5f, 0.5f, 0.5f), new Color(1, 0, 0, 0.5f),
                                        new Color(0, 0, 1, 0.5f),new Color(1, 1, 0, 0.5f),new Color(1, 0, 0, 0.5f)
                                     };

	/// <summary>
	/// SVNのログからステータス取得
	/// </summary>
	/// <param name="str"></param>
	/// <returns></returns>
	private static VCSStatus CheckStatus(string str)
	{
	
		str = str.TrimStart();

        string[] st = StatusString;
        if (Settings.VcsType == VCSType.HG)
        {
            st = StatusStringHG;
        }

        for (int i = 0; i < st.GetLength(0); i++)
        {
            string status = str.Substring(0, 2);
            int s = status.IndexOf(st[i]);
            if ( s > -1)
            {
                return (VCSStatus)i;
            }
        }
        return VCSStatus.NORMAL;
	}

    ///---------------------------------------------------------------------
	/// <summary>
	/// 状態アイコン表示
	/// </summary>
	/// <param name="selectionRect"></param>
	private static void ShowStatusLabel(VCSStatus status, Rect selectionRect)
	{
		// アイコンの位置
		const int size = 13;
		var pos = selectionRect;
		pos.y = selectionRect.position.y;
		pos.x = selectionRect.position.x;
		pos.width = size;
		pos.height = size;
		pos.yMin++;

		// ラベル表示
        string text = StatusIconString[(int)status];
        Color mColor = StatusIconColor[(int)status];

        if (status == VCSStatus.NORMAL){
            if (Settings.IconOverlayOnlyModified)
            {
                return;
            }
        }

		GUIStyleState styleState = new GUIStyleState();
		styleState.textColor = Color.white;

		GUIStyle style = new GUIStyle();
		style.fontSize = 14;
		style.fontStyle = FontStyle.Bold;
		style.normal = styleState;


		float offset = -12 + 3 * (selectionRect.height / 16.0f);
		// 小さいアイコンのときは端っこに寄せる
		pos.y = selectionRect.height > 16.0f ? selectionRect.position.y + offset : selectionRect.position.y+1;
		pos.x = selectionRect.height > 16.0f ? selectionRect.position.x + offset : 0;


		// BG
		var color = GUI.color;
		GUI.color = mColor;
		GUI.DrawTexture(pos, EditorGUIUtility.whiteTexture);
		GUI.color = color;

		// テキストは少し中に寄せる
		pos.y -= 2;
		pos.x += 2;

		// アイコン
		GUI.Label(pos, text, style);



	}

    ///---------------------------------------------------------------------
	/// <summary>
	/// SVNの状態表示更新
	/// フォルダとファイルは別管理。
	/// TortoiseSVNと同じようにサブホルダに変更ファイルがあれば親に状態が伝播する
	/// 初期化・アセット選択時に更新
	/// </summary>
    ///---------------------------------------------------------------------
	private static string svnupdatepath = "";
	private static Hashtable FileStatusMap = new Hashtable();
	private static Hashtable FolderStatusMap = new Hashtable();
    private static bool IsUpdateSuccess = false;

	private static void VCSStatusUpdate(string dataPath)
	{


		var path = dataPath;

		// ファイルのときはルートフォルダ
		if (path.IndexOf(".") != -1)
		{
			var startIndex = path.LastIndexOf("/");
			if (startIndex == -1) startIndex = path.Length;
			path = path.Substring(0, startIndex);
		}


		// 既に更新済み
		if (path == svnupdatepath)
		{
			return;
		}


		// 念のため最大数超えたら一旦クリア
        if (FileStatusMap.Count > 1024)
        {
			FileStatusMap.Clear();
		}

		//---------------------------------------------
		// ファイルの状態取得
		//---------------------------------------------
        string st = execGetStatus("status " + consoleCmd[(int)Settings.VcsType], path, "");
        // エラー?
        if (st == "error")
        {
            FileStatusMap.Clear();
        }

		// ログ整形
		st = st.Replace("\r", "");
		st = st.Replace("\r", "");
		st = st.Replace("\\", "/");
		var files = st.Split('\n');

		{
			// ステータス更新
			foreach (string fileStatus in files)
			{
				if (fileStatus == "") continue;
				var sIndex = fileStatus.IndexOf(ASSET_ROOT_DIR);
				if (sIndex == -1) sIndex = fileStatus.IndexOf(ASSET_ROOT);
				if (sIndex == -1)
				{
					// ignoreとか。。。
					continue;
				}

				string filepath = fileStatus.Substring(sIndex);
                
                // gitだと移動した時移動前と後のファイル名が出力される...
                if (filepath.IndexOf("->") > -1)
                {
                    filepath = filepath.Substring(filepath.IndexOf("-> ")+3);

                }

				{
					VCSStatus status = CheckStatus(fileStatus);

					if (filepath.IndexOf(".") != -1)
					{
						// metaファイルだけ更新されてるときチェック
						if (filepath.IndexOf(".meta")>-1)
						{
							string assetmeta = filepath.Replace(".meta", "");
                            VCSStatus assetstatus = FileStatusMap.Contains(assetmeta) ? (VCSStatus)FileStatusMap[assetmeta] : VCSStatus.UNMANAGE;
							if (status != VCSStatus.NORMAL && assetstatus == VCSStatus.NORMAL )
							{
								FileStatusMap[assetmeta] = status;
							}
						}


						FileStatusMap[filepath] = status;
					}
					else
					{
						FolderStatusMap[filepath] = status;
					}
				}
			}
		}

		//---------------------------------------------
		// 変更されたファイルが存在するフォルダだけチェック
		//---------------------------------------------
        st = execGetStatus("status " + consoleCmdDir[(int)Settings.VcsType], path, "");
        // エラー?
        if (st == "error")
        {
            FolderStatusMap.Clear();
        }

		// ログ整形
		st = st.Replace("\r", "");
		st = st.Replace("\r", "");
		st = st.Replace("\\", "/");
		var changefiles = st.Split('\n');

		// ステータス更新
		foreach (string fileStatus in changefiles)
		{
			if (fileStatus == "") continue;
			var sIndex = fileStatus.IndexOf(ASSET_ROOT_DIR);
			if (sIndex == -1) sIndex = fileStatus.IndexOf(ASSET_ROOT);
			if (sIndex == -1) {
				// ignoreとか。。。
				continue;
 			}
			string filepath = fileStatus.Substring(sIndex);


			// ファイルのときはルートフォルダに状態を伝播
			var startIndex = filepath.LastIndexOf("/");
			if (startIndex == -1) startIndex = filepath.Length;
			filepath = filepath.Substring(0, startIndex);


			VCSStatus status = CheckStatus(fileStatus);

			// フォルダが消されたみたいに見えるので、、、
			if (status != VCSStatus.NORMAL && status != VCSStatus.CONFRICT)
			{
				status = VCSStatus.EDIT;
			}

			// ルートフォルダまで同じステータスに
			filepath = filepath.Replace(ASSET_ROOT_DIR, "");
			var dirTree = filepath.Split('/');

			string dirpath = ASSET_ROOT_DIR;
			foreach (var dir in dirTree)
			{
				dirpath += dir;

                if (!FolderStatusMap.Contains(dirpath) ||
                    (VCSStatus)FolderStatusMap[dirpath] == VCSStatus.NORMAL)
                {
                    FolderStatusMap[dirpath] = status;
                }
 
				dirpath += "/";
			}

		}
	
		// アセットフォルダは消しとく。
		FolderStatusMap.Remove(ASSET_ROOT);

        // ステータス更新失敗したらしい。。
        if (FolderStatusMap.Count  == 0 && FileStatusMap.Count == 0)
        {
            IsUpdateSuccess = false;
            Debug.LogWarning("uVCSBridge Status Update Error.");
        }
        else
        {
            if (!IsUpdateSuccess)
            {
                Debug.Log("uVCSBridge Status Update Success.");
            }
            IsUpdateSuccess = true;
        }



		svnupdatepath = path;

        if( ProjectWindow ) ProjectWindow.Repaint();

	}

	/// <summary>
	/// ステータスアイコンをプロジェクトビューにオーバーレイ表示
	/// </summary>
	/// <param name="guid"></param>
	/// <param name="selectionRect"></param>
	private static void OnGUI(string guid, Rect selectionRect)
	{

        if ( Settings.IconOverlay)
        {
		    // ステータス更新
		    //var current = Event.current;
		    //if (current.type == EventType.MouseDown)
			if (IsSelectAsset && SelectAssetPaths[0] != "")
		    {
			    VCSStatusUpdate(SelectAssetPaths[0]);
		    }

		    var asset = AssetDatabase.GUIDToAssetPath(guid);


            if (IsUpdateSuccess) {
		        if (FileStatusMap.ContainsKey(asset))
		        {
			        // アイコン表示(ファイル)
			        VCSStatus status = (VCSStatus)FileStatusMap[asset];
                    ShowStatusLabel(status, selectionRect);
		        }
		        else if (FolderStatusMap.ContainsKey(asset))
		        {
			        // アイコン表示(フォルダ)
			        VCSStatus status = (VCSStatus)FolderStatusMap[asset];
                    ShowStatusLabel(status, selectionRect);
                }
                else
                {
                    if (asset != "" && Settings.VcsType != VCSType.SVN)
                    {
                       ShowStatusLabel(VCSStatus.NORMAL, selectionRect);
                    }
                }
            }
        }
	}



    ///---------------------------------------------------------------------
	/// <summary>
	/// 右クリックメニュー
	/// </summary>
    ///---------------------------------------------------------------------
    static bool IsManaged()
    {
        List<string> filepathArray = SelectAssetPaths;
        // 複数ファイル対応
        foreach (var asset in filepathArray)
        {

            if (FileStatusMap.ContainsKey(asset))
            {
                VCSStatus status = (VCSStatus)FileStatusMap[asset];
                if (status == VCSStatus.UNMANAGE)
                {
                    return false;
                }
            }
            else if (FolderStatusMap.ContainsKey(asset))
            {
                VCSStatus status = (VCSStatus)FolderStatusMap[asset];
                if (status == VCSStatus.UNMANAGE)
                {
                    return false;
                }
            }

        }

        return true;
    }


    const int MENU_PRIORITY = 900;
   

    /// <summary>
    /// ステータス更新
    /// </summary>
    /// <returns></returns>
    [MenuItem("Assets/uVCSBridge/Status Update", false, MENU_PRIORITY-20)]
    public static void statusUpdate()
    {
        VCSStatusUpdate(ASSET_ROOT_DIR);
    }
    
    
    /// <summary>
    /// 追加
    /// </summary>
    /// <returns></returns>
    [MenuItem("Assets/uVCSBridge/Add", true)]
    static bool enableadd()
    {
        return !IsManaged();
    }

    [MenuItem("Assets/uVCSBridge/Add", false, MENU_PRIORITY)]
    public static void add()
    {
        execTortoiseProc("add");
    }


    /// <summary>
    /// 更新
    /// </summary>
    [MenuItem("Assets/uVCSBridge/Update", false, MENU_PRIORITY)]
	public static void update()
	{
        if (Settings.VcsType == VCSType.GIT )
        {
            execTortoiseProc("pull");
        }
        else
        {
            execTortoiseProc("update");
        }
	}


    [MenuItem("Assets/uVCSBridge/Commit", false, MENU_PRIORITY)]
	public static void commit()
	{
		execTortoiseProc("commit");
	}



    [MenuItem("Assets/uVCSBridge/Push", true)]
    static bool enablepush()
    {
        return (Settings.VcsType != VCSType.SVN);
    }
    [MenuItem("Assets/uVCSBridge/Push", false, MENU_PRIORITY)]
    public static void push()
    {
        execTortoiseProc("push");
    }


    /// <summary>
    /// ログ
    /// </summary>
    [MenuItem("Assets/uVCSBridge/Log", true)]
    static bool enablelog()
    {
        return IsManaged();
    }
    [MenuItem("Assets/uVCSBridge/Log", false, MENU_PRIORITY)]
    public static void log()
    {
        execTortoiseProc("log");
    }






    /// <summary>
    /// クリーンナップ
    /// </summary>
    ///     
    [MenuItem("Assets/uVCSBridge/CleanUp", true)]
    static bool enableclean()
    {
        return IsManaged();
    }

    [MenuItem("Assets/uVCSBridge/CleanUp", false, MENU_PRIORITY)]
    public static void clean()
    {
        execTortoiseProc("cleanup");
    }








    /// <summary>
    /// 元に戻す
    /// </summary>
    /// <returns></returns>
    [MenuItem("Assets/uVCSBridge/Undo", true)]
    static bool enablerevert()
    {
        return IsManaged();
    }

    [MenuItem("Assets/uVCSBridge/Undo", false, MENU_PRIORITY)]
    public static void revert()
    {
        execTortoiseProc("revert");
    }






    /// <summary>
    /// リネーム
    /// </summary>
    [MenuItem("Assets/uVCSBridge/Rename", true)]
    static bool enablerename()
    {
        return IsManaged();
    }
    [MenuItem("Assets/uVCSBridge/Rename", false, MENU_PRIORITY)]
	public static void rename()
	{
		execTortoiseProc("rename");
	}


    /// <summary>
    /// 削除
    /// </summary>
    [MenuItem("Assets/uVCSBridge/Delete", true)]
    static bool enabledel()
    {
        return IsManaged();
    }

    [MenuItem("Assets/uVCSBridge/Delete")]
    public static void del()
    {
        execTortoiseProc("remove");
    }



    // Unity標準の仕様がちょっと気に入らないので。。。
#if UNITY_EDITOR_WIN
    [MenuItem("Assets/Open in Explorer", false, MENU_PRIORITY)]
#else
        [MenuItem("Assets/Open Folder", false, MENU_PRIORITY)]
#endif
    public static void explorer()
    {

#if UNITY_EDITOR_WIN
        System.Diagnostics.Process p = new System.Diagnostics.Process();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.CreateNoWindow = true;  // コンソール・ウィンドウを開かない
        p.StartInfo.UseShellExecute = false;    //シェル機能を使用しない
        p.StartInfo.RedirectStandardOutput = false; // 標準出力をリダイレクト

        string path = getFullPaths()[0];
        path = path.Replace("/", "\\");

        if (Directory.Exists(path))
        {
            // フォルダ
            p.StartInfo.Arguments = @"""" + path + @"""";
        }
        else
        {
            // ファイル
            p.StartInfo.Arguments = "/select," + @"""" + path + @"""";
        }

        p.Start();
        //p.WaitForExit();
#else
        string path = getFullPaths()[0];
        if (path.IndexOf(".") != -1){
            path = path.Substring(path.LastIndexOf("/"));
        }
        System.Diagnostics.Process.Start(path);
#endif
    }

    ///---------------------------------------------------------------------
    /// VCS実行
    ///---------------------------------------------------------------------


	/// <summary>
    /// TortoiseProc実行　.metaファイルも面倒みる。
	/// </summary>
	private static void execTortoiseProc(string command)
    {

		try
		{
			List<string> filepathArray = getFullPaths();
			string filepath = filepathArray[0];
			Debug.Log(command + ":" + filepath);


			if (command != "rename" && Settings.VcsType != VCSType.HG)
			{
				// 複数ファイル対応
				filepath = "";
				foreach (var file in filepathArray)
				{
					filepath += file;
					
					string dir = file.Substring(file.LastIndexOf("/"));
					if (dir != "/" + ASSET_ROOT)
					{
						// ファイルの時はmetaファイルも一緒に
						filepath += "*" + file + ".meta";
					}


					filepath += "*";
				}
			}

			// フォルダの場合はmetaファイルを先にリネーム
			if (command == "rename" && Directory.Exists(filepath))
			{
				string meta = filepath + ".meta";
				// プロセスが終了するまで待つ
				System.Diagnostics.Process pmeta = new System.Diagnostics.Process();
				pmeta.StartInfo.FileName = tortoiseProc[(int)Settings.VcsType];

				if (Settings.VcsType == VCSType.HG)
				{
					pmeta.StartInfo.Arguments = command + @" """ + meta + @"""";
				}
				else
				{
					pmeta.StartInfo.Arguments = "/command:" + command + " /path:\"" + meta + "\"" + " /closeonend:0";
				}
				pmeta.Start();
				pmeta.WaitForExit();
			}


			// HGのGUI向けコマンドは複数ファイル未対応？
			if (Settings.VcsType == VCSType.HG)
			{
				// プロセスが終了するまで待つ
				System.Diagnostics.Process p = new System.Diagnostics.Process();
				p.StartInfo.FileName = tortoiseProc[(int)Settings.VcsType];
				p.StartInfo.Arguments = command + @" """ + filepath + ".meta" + @"""";
				p.Start();
				//p.WaitForExit();
			}

			{
				// プロセスが終了するまで待つ
				System.Diagnostics.Process p = new System.Diagnostics.Process();
				p.StartInfo.FileName = tortoiseProc[(int)Settings.VcsType];
				if (Settings.VcsType == VCSType.HG)
				{
					p.StartInfo.Arguments = command + @" """ + filepath + @""""; ;
				}
				else
				{
					p.StartInfo.Arguments = "/command:" + command + " /path:\"" + filepath + "\"" + " /closeonend:0";
				}
				p.Start();
				p.WaitForExit();


                // 更新
				VCSStatusUpdate(ASSET_ROOT_DIR);
                VCSStatusUpdate(filepath);
			}
			return;
		}
		catch
		{
			// Tortoiseが無い場合とりあえずコンソール起動しとく
			string filepath = getFullPaths()[0];
			// プロセスが終了するまで待つ
			System.Diagnostics.Process p = new System.Diagnostics.Process();
			p.StartInfo.FileName = "cmd";
			p.StartInfo.WorkingDirectory = filepath;
			//p.StartInfo.Arguments = " --version";
			p.StartInfo.Arguments = command;
			p.StartInfo.CreateNoWindow = false;  // コンソール・ウィンドウを開かない
			p.StartInfo.UseShellExecute = false;    //シェル機能を使用しない
			//p.StartInfo.RedirectStandardOutput = true; // 標準出力をリダイレクト
			//p.StartInfo.RedirectStandardError = true;  // エラーリダイレクト →svnでコレ有効にすると無限ループ...

			p.Start();

			Debug.LogWarning("not find " + tortoiseProc[(int)Settings.VcsType]);
			return;
		}

    }

	/// <summary>
	/// ステータス取得
	/// パスが通ってれば使える
	/// 汎用的に使えるようにしようとして諦めた。。。
	/// </summary>
	private static string execGetStatus(string command, string arg1, string arg2)
	{
		try
		{
			string filepath = arg1;

			// ファイルの時はmetaファイルも一緒に
			if (command.IndexOf("status") == -1 && command.IndexOf("move") == -1)
			{
				filepath += "*" + filepath + ".meta";
			}

			// フォルダの場合はmetaファイルを先にリネーム
			if (command == "move" && Directory.Exists(filepath))
			{
				string meta = filepath + ".meta";
				// プロセスが終了するまで待つ
				System.Diagnostics.Process pmeta = new System.Diagnostics.Process();
                pmeta.StartInfo.FileName = consoleExe[(int)Settings.VcsType];
				//pmeta.StartInfo.WorkingDirectory = workPath; 
				pmeta.StartInfo.Arguments = command + @" """ + "./" + meta + @""""; ;// +" ./" + arg2 + ".meta";
				pmeta.StartInfo.CreateNoWindow = true;  // コンソール・ウィンドウを開かない
				pmeta.StartInfo.UseShellExecute = false;    //シェル機能を使用しない
				pmeta.StartInfo.RedirectStandardOutput = false; // 標準出力をリダイレクト
				pmeta.Start();

				//pmeta.WaitForExit();
			}

			// プロセスが終了するまで待つ
			System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo.FileName = consoleExe[(int)Settings.VcsType];
			//p.StartInfo.WorkingDirectory = workPath;
			//p.StartInfo.Arguments = " --version";
			p.StartInfo.Arguments = command + @" """ + "./" + filepath + @"""";// +" ./" + arg2;
			p.StartInfo.CreateNoWindow = true;  // コンソール・ウィンドウを開かない
			p.StartInfo.UseShellExecute = false;    //シェル機能を使用しない
			p.StartInfo.RedirectStandardOutput = true; // 標準出力をリダイレクト
            //p.StartInfo.RedirectStandardError = true;  // エラーリダイレクト →svnでコレ有効にすると無限ループ...

			p.Start();
			//p.WaitForExit();
            //string error = p.StandardError.ReadToEnd();


			return p.StandardOutput.ReadToEnd(); // 標準出力の読み取り;

		}
		catch
		{
            return "error";
		}

	}

}
