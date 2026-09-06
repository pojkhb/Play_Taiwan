using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace backend.Models
{
    // ===================== ?餃 / 撣唾? =====================
    public class LoginRequest
    {
        public string ep_name { get; set; }      // ?Ｗ隞??
        public string ep_pswd { get; set; }      // ??撖Ⅳ
    }

    public class LoginResponse
    {
        public string token { get; set; }        // JWT ?餃甈?
        public string ep_id { get; set; }        // ?Ｗ隞??
        public string ep_name { get; set; }      // 撣唾??迂 (?舐楊頛?
         public int account_type { get; set; }          // ???詨?嚗?=?拙振嚗?=?振
          public string account_type_name { get; set; }   // 頧?敺?摮?Tourist / 
    }

    public class EpAccount
    {
        public string ep_id { get; set; }           // ?Ｗ/?振隞?? (UUID嚗頂蝯梯?????
        public string ep_name { get; set; }         // ?Ｗ/?振憿舐內?迂 (?餃?雿輻)??
        public int account_type { get; set; }       // 撣唾?頨怠?嚗? = ?恥(Tourist)嚗? = ?振(Merchant)??
        public string email { get; set; }           // ?餃?靽∠拳 (閮餃???交?雿輻)??
        public string ep_pswd { get; set; }         // 鞈?摨怠?脣???HMAC SHA256 撖Ⅳ????
        public bool is_active { get; set; }         // 撣唾?蝮賡????臬? (true=?, false=?)??
        public string email_token { get; set; }     // 靽∠拳撽?撠?摮?Token (撽???敺?蝛???
        public bool is_email_verified { get; set; } // 靽∠拳?臬撌脤?撽? (true=撌脤?霅? false=?芷?霅???
    }

    public class EpAccountUpdateRequest
    {
        public string ep_name { get; set; }   // 甈脫?啁??Ｗ/?振憿舐內?迂
    }

    // ===================== 擐?蝮質汗 =====================
    public class HomeOverviewResponse
    {
        public int completed_story_count { get; set; }       // 撌脣???祆
        public int postcard_count { get; set; }               // 撌脫??靽∠???
        public int badge_count { get; set; }                   // 撌脩敺噬蝡
        public int vlog_count { get; set; }                     // 撌脩???VLOG ??
        public List<HomeCardItem> recent_cards { get; set; } // ?箔遙?ａ/????蝑??????
    }

    public class HomeCardItem
    {
        public string card_id { get; set; }       // ?∠?霅蝣?
        public string card_type { get; set; }     // ?∠?憿?嚗?憒?"?箔遙?ａ" / "????
        public string title { get; set; }          // ?∠?璅?
        public string image_url { get; set; }      // ?∠???蝬脣?
    }

    // ===================== ? / ?豢?璅∪? =====================
    public class StoryGenerateRequest
{
    public string city_name { get; set; }
    public string town_name { get; set; }
    public int traveler_count { get; set; }
    public List<string> preferences { get; set; }
    public List<string> transportation { get; set; }
    public int node_count { get; set; }
    public bool is_night { get; set; }
    
    // ?啣?嚗?瘙?AI ?????祆??
    public int story_count { get; set; } 
}

    public class StoryWheelSpinResponse
    {
        public string region_id { get; set; }      // ?啣??箏?隞??嚗?憒?region_tainan_anping
        public string region { get; set; }          // ?垢憿舐內???迂嚗?憒??啣?摰像
        public string city_name { get; set; }        // ?撅砍?撣?蝔梧?靘? ?箏?撣?
        public string district_name { get; set; }    // ?撅祈??踹??迂嚗?憒?摰像?
    }

    public class StoryOptionResponse
    {
        public string story_id { get; set; }               // ?隞??
        public string title { get; set; }                   // ?璅?
        public string prologue { get; set; }                // ???
        public string category { get; set; }                // ??: ?/蝢??ａ
        public string transport { get; set; }                // ??鈭日極??
        public List<string> expected_badges { get; set; }   // ???舐敺?敺賜??迂皜
        public int expected_postcards { get; set; }           // ???舀???縑???
        public string region_id { get; set; }                 // ?啣?隞??
        public string region { get; set; }                     // ?啣??迂
        public List<string> route_preview { get; set; }      // ?Ｙ揣頝舐??汗(?舫??迂皜)

        public class RouteNode
        {
            public string node_id { get; set; }         // 蝭暺誨??
            public string location_name { get; set; }    // ?舫??迂
            public int node_order { get; set; }            // 蝭暺?摨?
        }
    }

    public class StoryConfirmRequest
    {
        public string story_id { get; set; }   // 甈脩Ⅱ隤?憪??隞??
    }

    // ===================== ?啣? / ?Ｙ揣?脣漲 =====================
    public class MapResponse
    {
        public string story_id { get; set; }                    // ?隞??
        public int unlocked_node_count { get; set; }             // 撌脰圾??暺
        public int total_node_count { get; set; }                 // 蝮賜?暺
        public int postcard_unlocked_count { get; set; }          // 撌脰圾??靽∠???
        public int postcard_total_count { get; set; }              // 蝮賣?靽∠???
        public List<MapNode> nodes { get; set; }                 // ?啣?蝭暺???
        public int day_index { get; set; }                        // 蝚砌???蝚砌??亦??桀?憭拇蝝Ｗ?
        public int total_days { get; set; }                        // ?蝮賢予??
    }

    public class MapNode
    {
        public string node_id { get; set; }                    // 蝭暺誨??
        public string location_name { get; set; }               // ?舫??迂
        public double lat { get; set; }                          // 蝺臬漲
        public double lng { get; set; }                          // 蝬漲
        public bool is_unlocked { get; set; }                    // ?臬撌脰圾??
        public bool is_night_only { get; set; }                   // ?臬?箏??????曄?蝭暺?
        public string fog_hint { get; set; }                      // ?脤??梁??膩
        public int day_index { get; set; } // 蝭暺?撅砍予?賂??垢?其???蝚砌??伐?蝚砌???
        public List<string> child_node_ids { get; set; }        // 璅寧????蝭暺誨????
        public string image_url { get; set; }                    // 撌脰圾?暺???
        public string silhouette_image_url { get; set; }        // ?芾圾??憿舐內?敶勗?
        public int node_order { get; set; }                       // ?垢?怨楝蝺??摨?
    }

    public class NodeDetailResponse
    {
        public string node_id { get; set; }                 // 蝭暺誨??
        public string location_name { get; set; }            // ?舫??迂
        public string npc_name { get; set; }                  // 閰脩?暺??? NPC ?迂
        public string intro_story { get; set; }               // NPC 隞晶??
        public string opening_hours { get; set; }              // ?舫????
        public List<string> nearby_food { get; set; }        // ?券?蝢??刻皜
        public string task_id { get; set; }                    // 閰脩?暺???隞餃?隞??
        public string review_story_url { get; set; }          // ?“?????
    }

    public class NpcInteractionResponse
    {
        public string node_id { get; set; }               // 蝭暺誨??

        // ?圈?嚗??
        public string location_name { get; set; }          // ?舫??迂
        public string location_subtitle { get; set; }       // ?舫??舀?憿?
        public string scene_image_url { get; set; }          // ?湔??雯?

        // NPC
        public string npc_id { get; set; }                    // NPC 隞??
        public string npc_name { get; set; }                   // NPC ?迂
        public string npc_avatar_url { get; set; }              // NPC ?剖?蝬脣?

        // 撠店
        public string npc_dialogue { get; set; }                // NPC 撠店??
        public string emotion { get; set; }                      // normal / happy / hint

        // ?垢????蝥?雿?
        public string skip_button_text { get; set; }             // 頝喲???憿舐內??
        public string next_task_id { get; set; }                  // ?冽?鈭?璅⊥憟?閮???銝??遙?誨??
    }

    public class NavigationRequest
    {
        public string node_id { get; set; }   // 甈脣??芸?敺??暺誨??
    }

    public class NavigationResponse
    {
        public string maps_deeplink_url { get; set; }   // Google Maps 撠???
    }

    // ===================== 隞餃? / 蝑? / ?內 =====================
    public class TaskListReq
    {
        public string node_id { get; set; } // 甈脩??遙??蝭暺誨??
        public float gps_lon { get; set; } // ?拙振?桀?蝬漲
        public float gps_lat { get; set; } // ?拙振?桀?蝺臬漲
        public int player_count { get; set; } // ?鈭箸
    }

    public class SearchNeo4jReq
    {
        public string place_id { get; set; } // 甈脫閰Ｙ??舫?隞??
        public string story_id { get; set; } // ?撅砍??砌誨??
    }

    /// <summary>
    /// 蝪∪?敺?隞餃?閰單???
    /// </summary>
    public class TaskDetailResponse
    {
        public int task_id { get; set; }
        public string story_id { get; set; }
        public string node_id { get; set; }
        public string task_place_id { get; set; }
        public int type_id { get; set; }
        public string task_type { get; set; }
        
        public string task_describe { get; set; } // AI ???遙?摰?
        
        public List<TaskOption> options { get; set; } // ?豢?憿??
        public List<string> media_urls { get; set; } // 雿輻???喟???/敶梁?
    }

    public class TaskOption
    {
        public string option_key { get; set; }   // ?璅? (A,B,C)
        public string option_text { get; set; }  // ?賊???
        public string option_url { get; set; }   // ?賊???蝬脣?(?交?)
        [JsonIgnore]
        public bool is_correct { get; set; }     // ?臬?箸迤閫?(銝??喟策?垢)
    }

                public class TaskAnswerRequest
    {
        public int task_id { get; set; }                           // 任務代號 (md_task.task_id)
        
        public string selected_option_key { get; set; }            // 選擇題型 (A/B/C)
        
        public string text_answer { get; set; }                    // 文字問答型
        
        public string photo_url { get; set; }                      // 照片上傳型
        
        public string video_url { get; set; }                      // 短片演繹型
        
        public string audio_url { get; set; }                      // 採訪蒐證型

        [System.Text.Json.Serialization.JsonIgnore]
        public string ep_id { get; set; }                          // 後端從 Token 取得
    }

    public class TaskAnswerResponse
    {
        public bool is_correct { get; set; }                  // 蝑??臬甇?Ⅱ
        public bool is_pending_review { get; set; }             // ?臬?犖撌亙祟?訾葉(?剔?瞍凳?縑敹?頞單?)
        public string feedback_message { get; set; }             // 蝯衣摰嗥?????
        public string unlocked_postcard_id { get; set; }          // 蝑?敺圾???縑?誨??
        public int unlocked_node_progress { get; set; }            // ?桀?撌脰圾??暺脣漲
        public int total_node_count { get; set; }                   // 閰脣??祉蜇蝭暺
    }

    public class TaskHintResponse
    {
        public string task_id { get; set; }             // 隞餃?隞??
        public string npc_avatar_url { get; set; }        // ?內撠店獢＊蝷箇?NPC?剖?
        public string hint_text { get; set; }              // ?內???批捆
        public bool is_available { get; set; }              // ?臬撌脤??啁??舀活?賊?瑼鳴??舫＊蝷箸?蝷?
    }

    // ===================== ?縑??=====================
    public class PostcardResponse
    {
        public string postcard_id { get; set; }        // ?縑?誨??
        public string title { get; set; }                // ?縑??憿?e.g. ?箏?摮?-?典擐飛?擐?
        public string subtitle { get; set; }               // ?縑?璅?
        public string front_image_url { get; set; }         // AI ??甇???蝬脣?
        public string back_photo_url { get; set; }            // ?拙振?????Ｙ?雯?
        public string culture_note { get; set; }                // ??閫?牧??
        public DateTime found_date { get; set; }                 // ???交?
        public bool is_night_edition { get; set; }                 // ?臬?箏???摰???
    }

    public class PostcardPrintRequest
    {
        public string postcard_id { get; set; }   // 甈脣??啁??縑?誨??
    }

    public class PostcardPrintResponse
    {
        public string ibon_pickup_code { get; set; }    // 撠? pincode (?辣蝣?
        public string pdf_url { get; set; }               // ????雯?
        public string deadline { get; set; }              // ???啣?嚗?隞嗆???
        public string qrcode_base64 { get; set; }         // ???啣?嚗RCode ??Base64 摮葡
    }

    public class PostcardShareRequest
    {
        public string postcard_id { get; set; }   // 甈脣?鈭怎??縑?誨??
        public string platform { get; set; }        // ?澈撟喳嚗?憒?IG 蝑?
    }

    // ===================== 敺賜? =====================
    public class BadgeResponse
    {
        public string badge_id { get; set; }             // 敺賜?隞??
        public string badge_name { get; set; }             // 敺賜??迂
        public string badge_type { get; set; }               // 敺賜?憿?嚗???舫?/蝟餃?
        public string image_url { get; set; }                 // 敺賜???蝬脣?
        public DateTime obtained_date { get; set; }             // ???交?
    }

    // ===================== Vlog / ?蝯? / ?“ =====================
    public class StoryEndingResponse
    {
        public string story_id { get; set; }                          // ?隞??
        public string title { get; set; }                               // ?璅?嚗?憒?摨????仃?賢
        public int walked_steps { get; set; }                            // 頝?甇交
        public string task_completion_ratio { get; set; }                 // ?渲圾雓?瘥?嚗?憒?16/16
        public string postcard_completion_ratio { get; set; }              // 撠?縑??靘?靘? 10/10
        public string ending_type { get; set; }                             // 蝯?憿?嚗??祉?撅/?梯?蝯?
    }

    public class VlogGenerateRequest
    {
        public string story_id { get; set; }   // 甈脩???Vlog ???砌誨??
    }

    public class VlogResponse
    {
        public string vlog_id { get; set; }              // Vlog 隞??
        public string story_id { get; set; }               // ?撅砍??砌誨??
        public string video_url { get; set; }                // 敶梁?蝬脣?
        public string thumbnail_url { get; set; }              // 敶梁?蝮桀?蝬脣?
        public DateTime completed_date { get; set; }            // ??摰??交?
    }

    // ===================== ?? (History) =====================
    public class HistoryStoryItem
    {
        /// <summary>?隞??嚗???md_story.story_id嚗?憒?story_tainan_001??/summary>
        public string story_id { get; set; }                    // ?隞??

        /// <summary>?璅???/summary>
        public string title { get; set; }                        // ?璅?

        /// <summary>?蝪∩?????/summary>
        public string synopsis { get; set; }                      // ?蝪∩?

        /// <summary>甇文??砍????交?????/summary>
        public DateTime completed_date { get; set; }               // 摰??交?

        /// <summary>??撅砍??迂嚗?憒?偶摨瑕???/summary>
        public string region { get; set; }                          // ?撅砍??迂

        /// <summary>?Ｙ揣頝舐???嚗?摨??箇摰嗉粥???舫??迂??/summary>
        public List<string> route_summary { get; set; }              // ?Ｙ揣頝舐???(?舫??迂皜)

        /// <summary>甇文??砍????? Vlog 隞??嚗撠??? null??/summary>
        public string vlog_id { get; set; }                            // 撠???Vlog 隞??

        /// <summary>?縑??憿折??Ｙ????嚗?∪???null??/summary>
        public string postcard_review_url { get; set; }                 // ?縑??憿折??
    }

    // ===================== ?嗉? =====================
    public class FavoriteItemResponse
    {
        public string favorite_id { get; set; }     // ?嗉?蝝?誨??
        public string item_type { get; set; }         // ?嗉??憿?嚗ostcard/badge/vlog
        public string ref_id { get; set; }              // 撠???祕?誨??
        public string image_url { get; set; }            // 蝮桀?蝬脣?
        public string title { get; set; }                  // 憿舐內璅?
    }

    // ===================== ?券?憟賢 (隞餃?/?券?鞈?) =====================
    public class NearbyPlaceResponse
    {
        public string place_id { get; set; }                // ?舫?/摨振隞??
        public string category { get; set; }                  // ??嚗ㄡ憌??嗡?
        public string name { get; set; }                        // ?迂
        public string address { get; set; }                      // ?啣?
        public string open_time { get; set; }                      // ?/?平??
        public List<string> photo_urls { get; set; }              // ?抒?蝬脣?皜
        public string maps_deeplink_url { get; set; }                // Google Maps 撠???
    }

    /// <summary>
    /// ?剔?瞍凳??憪踹瘥?蝭鞈?嚗???md_task.pose_reference_json 閫??敺?蝯???
    /// </summary>
    public class PoseReference
    {
        public List<double> JointAngles { get; set; } = new(); // 蝭????蝭閫漲摨?
    }

    /// <summary>
    /// ?∟赤???????摮?穿?撠? md_task.interview_script_json 閫??敺?蝯???
    /// </summary>
    public class InterviewScript
    {
        public List<string> ExpectedKeywords { get; set; } = new(); // 摨振/NPC??銝剜?????萄?皜
    }

    /// <summary>
    /// 頝券?????閫??璇辣嚗???md_task.hidden_unlock_condition_json 閫??敺?蝯???
    /// </summary>
    public class CrossLevelCondition
    {
        public List<string> RequiredTaskIds { get; set; } = new(); // 蝯?閫??????蝵桅??﹀ask_id皜
    }

    /// <summary>
    /// ?梯??閫貊瑼Ｘ蝯?????md_hidden_level 銵剁??潛摰詖PS??◤?孛?潘?銝?憭??曆蜓?閰Ｕ?
    /// </summary>
    public class HiddenLevelTriggerResult
    {
        public bool triggered { get; set; }               // ?活瑼Ｘ?臬閫貊鈭?????
        public string hidden_level_id { get; set; }        // 閫貊????∩誨??
        public string title { get; set; }                    // ?梯??璅?
        public string cultural_background { get; set; }       // ?典甇瑕/???隤芣?
        public string content { get; set; }                     // ?舐????批捆
        public string reward_badge_id { get; set; }               // 閫貊敺?賜策鈭?敺賜?隞??
        public string reward_postcard_id { get; set; }              // 閫貊敺?賜策鈭??縑?誨??
    }
    
}
