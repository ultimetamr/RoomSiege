using System.Text;
using RoomSiege.Data;
using RoomSiege.Enemies;
using UnityEngine;

namespace RoomSiege.Waves
{
    public static class WaveThreatPresentation
    {
        // Mirrors the existing scheduler's even split and rotating group start.
        public static int EntranceCount(int groupCount,int groupOrdinal,int entrance,int entrances)
        {
            if(groupCount<=0||entrances<=0||entrance<0||entrance>=entrances)return 0;
            int slot=(entrance-groupOrdinal%entrances+entrances)%entrances;
            return groupCount/entrances+(slot<groupCount%entrances?1:0);
        }

        public static string Forecast(WaveDefinition wave,int entrance,int entrances)
        {
            if(wave==null||wave.groups==null)return "";
            var text=new StringBuilder();
            for(int i=0;i<wave.groups.Length;i++)
            {
                int count=EntranceCount(wave.groups[i].count,i,entrance,entrances);
                if(count<=0)continue;
                if(text.Length>0)text.Append(" · ");
                text.Append(EnemyReadability.Name(wave.groups[i].enemyId)).Append("×").Append(count);
            }
            return text.Length>0?text.ToString():"本波无出怪";
        }

        public static string StrategyHint(WaveDefinition wave)
        {
            if(wave==null||wave.groups==null||wave.groups.Length==0)return "布防建议：先确认入口方向，再覆盖通往安全区的路线";
            bool runner=false,tank=false,shield=false,boss=false;
            foreach(var group in wave.groups)
            {
                runner|=group.enemyId=="Runner";tank|=group.enemyId=="Tank";
                shield|=group.enemyId=="Shielded";boss|=group.enemyId=="Boss";
            }
            if(boss)return "布防建议：Laser 集火首领护盾，Frost 延长输出，MG 清理召唤物";
            if(shield&&runner)return "布防建议：Laser 处理护盾，Frost＋Trap 拦截跑者";
            if(shield)return "布防建议：Laser 破盾，配合 Frost 把目标留在火力区";
            if(runner)return "布防建议：Trap 卡入口，Frost 减速后用 MG 收割";
            if(tank)return "布防建议：给 Tank 留出射程，优先升级持续输出塔";
            return "布防建议：用 Frost 控速，让 MG 覆盖主要路线";
        }

        public static string Direction(Vector3 forward,Vector3 delta)
        {
            forward.y=delta.y=0;
            if(delta.sqrMagnitude<.0001f)return "近处";
            if(forward.sqrMagnitude<.0001f)return "转头查看";
            float angle=Vector3.SignedAngle(forward,delta,Vector3.up);
            float magnitude=Mathf.Abs(angle);
            if(magnitude<22.5f)return "↑ 前方";
            if(magnitude>=157.5f)return "↓ 身后";
            if(magnitude<67.5f)return angle>0?"↗ 右前":"↖ 左前";
            if(magnitude<112.5f)return angle>0?"→ 右侧":"← 左侧";
            return angle>0?"↘ 右后":"↙ 左后";
        }
    }
}
