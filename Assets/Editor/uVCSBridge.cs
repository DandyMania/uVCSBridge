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
        //EditorGUILayout.LabelField("VCS Type" + "  ");
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
            VCSStatusUpdate(ASSET_ROOT);
            ProjectWindow.Repaint();
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
    static string[] tortoiseProc = { "TortoiseProc.exe", "TortoiseGitProc.exe", "thg.exe" };
    static string[] consoleExe = { "svn", "git", "hg" };
    static string[] consoleCmd = { "-v wc", "-v -u -s", "-v"};
    static string[] consoleCmdDir = { "wc", "-v -u -s", "-v"};


	// 選択中のオブジェクトが存在するかどうかを返します
	private const string REMOVE_STR = "Assets";
	
	private const string ASSET_ROOT = REMOVE_STR + "/";


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
			return ASSET_ROOT;
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
	private static string getFullPath()
	{
		var startIndex = SelectAssetPath.LastIndexOf(REMOVE_STR);
		var assetPath = SelectAssetPath.Remove(startIndex, REMOVE_STR.Length);
		return Application.dataPath + assetPath;
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
		VCSStatusUpdate(ASSET_ROOT);
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
    
    static string[] StatusIconString = { "o", "?", "!", "+", "!?", "x" };

    static Color[] StatusIconColor = {  new Color(0, 1, 0, 0.5f), new Color(1, 1, 1, 0.5f), new Color(1, 0, 0, 0.5f),
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

        for (int i = 0; i < StatusString.GetLength(0);i++ )
        {
            string status = str.Substring(0, 2);
            int s = status.IndexOf(StatusString[i]);
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
		const int size = 15;
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

		//var activePath = AssetDatabase.GUIDToAssetPath(guid);


		// 既に更新済み
		if (dataPath == svnupdatepath)
		{
			return;
		}

		var path = dataPath;

		// ファイルのときはルートフォルダ
		if (path.IndexOf(".") != -1)
		{
			var startIndex = path.LastIndexOf("/");
			if (startIndex == -1) startIndex = path.Length;
			path = path.Substring(0, startIndex);
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
				var sIndex = fileStatus.IndexOf(ASSET_ROOT);
				if (sIndex == -1) sIndex = fileStatus.IndexOf(REMOVE_STR);
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
			var sIndex = fileStatus.IndexOf(ASSET_ROOT);
			if (sIndex == -1) sIndex = fileStatus.IndexOf(REMOVE_STR);
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

			if (status == VCSStatus.DELETE)
			{
				status = VCSStatus.EDIT;
			}

			// ルートフォルダまで同じステータスに
			filepath = filepath.Replace(ASSET_ROOT, "");
			var dirTree = filepath.Split('/');

			string dirpath = ASSET_ROOT;
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
		FolderStatusMap.Remove(REMOVE_STR);

        // ステータス更新失敗したらしい。。
        if (FolderStatusMap.Count  == 0 && FileStatusMap.Count == 0)
        {
            IsUpdateSuccess = false;
            Debug.LogError("uVCSBridge Status Update Error.");
        }
        else
        {
            if (!IsUpdateSuccess)
            {
                Debug.Log("uVCSBridge Status Update Success.");
            }
            IsUpdateSuccess = true;
        }



		svnupdatepath = dataPath;

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
		    if (IsSelectAsset && SelectAssetPath != "")
		    {
			    VCSStatusUpdate(SelectAssetPath);
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
                    if (asset != "")
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
	[MenuItem("Assets/uVCSBridge/Update", false, 60)]
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


    [MenuItem("Assets/uVCSBridge/Commit", false, 60)]
	public static void commit()
	{
		execTortoiseProc("commit");
	}


    [MenuItem("Assets/uVCSBridge/Push", true)]
    static bool enablepush()
    {
        return (Settings.VcsType != VCSType.SVN);
    }
    [MenuItem("Assets/uVCSBridge/Push", false, 60)]
    public static void push()
    {
        execTortoiseProc("push");
    }


    [MenuItem("Assets/uVCSBridge/Log", false, 80)]
    public static void log()
    {
        execTortoiseProc("log");
    }

	// Unity標準の仕様がちょっと気に入らないので。。。
    [MenuItem("Assets/Open in Explorer", false, 60)]
	public static void explorer()
	{
		System.Diagnostics.Process p = new System.Diagnostics.Process();
		p.StartInfo.FileName = "explorer.exe";
		p.StartInfo.CreateNoWindow = true;  // コンソール・ウィンドウを開かない
		p.StartInfo.UseShellExecute = false;    //シェル機能を使用しない
		p.StartInfo.RedirectStandardOutput = false; // 標準出力をリダイレクト

		string path = getFullPath();
		path = path.Replace("/","\\");

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
	}


    [MenuItem("Assets/uVCSBridge/CleanUp", false, 80)]
	public static void clean()
	{
		execTortoiseProc("cleanup");
	}



    [MenuItem("Assets/uVCSBridge/Add", false, 100)]
	public static void add()
	{
		execTortoiseProc("add");
	}

    [MenuItem("Assets/uVCSBridge/Undo", false, 100)]
	public static void revert()
	{
		execTortoiseProc("revert");
	}

    [MenuItem("Assets/uVCSBridge/Rename", false, 100)]
	public static void rename()
	{
		execTortoiseProc("rename");
	}



    [MenuItem("Assets/uVCSBridge/Delete")]
	public static void del()
	{
		execTortoiseProc("remove");
	}



    ///---------------------------------------------------------------------
    /// VCS実行
    ///---------------------------------------------------------------------


	/// <summary>
    /// TortoiseProc実行　.metaファイルも面倒みる。
	/// </summary>
	private static void execTortoiseProc(string command)
    {
        string filepath = getFullPath();
        Debug.Log(command + ":" + filepath);

        // ファイルの時はmetaファイルも一緒に
        if (command != "rename" && Settings.VcsType != VCSType.HG)
        {
            filepath += "*" + filepath + ".meta";
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
			//var startIndex = Application.dataPath.LastIndexOf(REMOVE_STR);
			//var workPath = Application.dataPath.Remove(startIndex, REMOVE_STR.Length);

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

		return "";

	}

}
