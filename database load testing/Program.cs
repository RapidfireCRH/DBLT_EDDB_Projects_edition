using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace database_load_testing
{
    class Program
    {
        public static readonly int[] query_size = { 50, 61,  83,  127, 215, 391, 743, 1447, 2855, 5671};
        static readonly int usernum = 5;//Number of user to simulate
        static readonly int maxjumpnum = 10;
        public struct user_st
        {
            public string name;
            public int jumprange;//distance to add or remove from 
            public TimeSpan next_query_time;//time between queries
            public DateTime lastjump;//last jump date
            public double x;
            public double y;
            public double z;
            public int query;//0 - 10, which query it should do
            public int jumpnum;//current jump number
            public history_st[] history;
        }
        public struct history_st
        {
            public string query;
            public int resultnum;
            public TimeSpan time;
        }
        public struct star_st
        {
            public string name;
            public coord_st coord;
            public double distance(star_st other)
            {
                return Math.Sqrt(Math.Pow(this.coord.x - other.coord.x, 2) + Math.Pow(this.coord.y - other.coord.y, 2) + Math.Pow(this.coord.z - other.coord.z, 2));
            }
        }
        private struct check_st : IComparable<check_st>
        {
            public star_st star;
            public double dist;
            public int CompareTo(check_st other)
            {
                return this.dist.CompareTo(other.dist);
            }
        }
        public struct coord_st
        {
            public double x;
            public double y;
            public double z;
        }
        public static List<string> writer = new List<string>();
        static void Main()
        {
            //init
            Random r = new Random((int)DateTime.Today.Ticks);
            List<Thread> group = new List<Thread>();
            writer.Add("User, Jump Number, Query, Result Number, Delay");
            for(int i = 0; i!= usernum; i++)
            {
                user_st user = new user_st();
                user.name = "User " + i;
                user.jumprange = r.Next(17, 56);
                user.next_query_time = DateTime.Now.AddSeconds(r.Next(10, 30)) - DateTime.Now;
                user.lastjump = DateTime.Now;
                user.x = r.Next(-2000, 2001);
                user.y = r.Next(-2000, 2001);
                if (r.Next(0, 3) > 1)//coin flip
                    user.z = r.Next(23000, 28001);//core
                else
                    user.z = r.Next(-2000, 2000);//bubble
                user.query = 2;
                user.jumpnum = 0;
                user.history = new history_st[maxjumpnum];
                group.Add(new Thread(() => worker(user)));
            }
            int numofactivethreads = usernum;
            foreach (Thread x in group)
                x.Start();
            while (numofactivethreads != 0)
            {
                Console.Clear();
                Console.WriteLine("Simulation running, " + numofactivethreads + " users remaining.");
                numofactivethreads = 0;
                foreach(Thread x in group)
                    if (x.IsAlive)
                        numofactivethreads++;
                Thread.Sleep(1000);
            }
            if (!Directory.Exists("worker"))
                Directory.CreateDirectory("worker");
            File.WriteAllLines("worker\\work.csv", writer);
        }
        public static void worker(user_st user)
        {
            user_st save = work(user);
            if (!Directory.Exists("Workers"))
                Directory.CreateDirectory("Workers");
            for (int i = 4; i != (maxjumpnum + 4); i++)
                writer.Add(save.name + ", " + (i - 4) + ", " + save.history[i - 4].query + ", " + save.history[i - 4].resultnum.ToString() + ", " + save.history[i - 4].time.TotalMilliseconds);
        }
        public static user_st work(user_st user)
        {
            Random r = new Random(user.jumprange + (int)user.lastjump.Ticks);
            while (user.jumpnum != user.history.Length)
            {
                if (user.lastjump.Add(user.next_query_time) < DateTime.Now)
                {
                    //Adjust position
                    if (r.Next(0, 3) > 1)
                        user.x = user.x + user.jumprange;
                    else
                        user.x = user.x - user.jumprange;
                    if (r.Next(0, 3) > 1)
                        user.z = user.z + user.jumprange;
                    else
                        user.z = user.z - user.jumprange;
                    user.lastjump = DateTime.Now;
                    //create location for mathmatics
                    star_st curr = new star_st();
                    curr.coord.x = user.x;
                    curr.coord.y = user.y;
                    curr.coord.z = user.z;
                    //query
                    DateTime start = DateTime.Now;
                    user.history[user.jumpnum].query = "SELECT * FROM systems WHERE " +
                        "systems.x BETWEEN " + (user.x - query_size[user.query]) + " AND " + (user.x + query_size[user.query]) + " AND " +
                        "systems.y BETWEEN " + (user.y - query_size[user.query]) + " AND " + (user.y + query_size[user.query]) + " AND " +
                        "systems.z BETWEEN " + (user.z - query_size[user.query]) + " AND " + (user.z + query_size[user.query]);
                    NpgsqlConnection conn = new NpgsqlConnection("Pooling=false; SERVER=cyberlord.de; Port=5432; Database=edmc_rse_db; User ID=edmc_rse_user; Password=asdfplkjiouw3875948zksmdxnf;Timeout=12;Application Name=stresstest-" + user.name);
                    conn.Open();
                    NpgsqlTransaction tran = conn.BeginTransaction();
                    NpgsqlCommand command = new NpgsqlCommand(user.history[user.jumpnum].query, conn);
                    NpgsqlDataReader read = command.ExecuteReader();
                    while (read.Read())
                    {
                        check_st ret = new check_st();
                        ret.star.name = read["name"].ToString();
                        ret.star.coord.x = Double.Parse(read["x"].ToString(), CultureInfo.InvariantCulture);
                        ret.star.coord.y = Double.Parse(read["y"].ToString(), CultureInfo.InvariantCulture);
                        ret.star.coord.z = Double.Parse(read["z"].ToString(), CultureInfo.InvariantCulture);
                        ret.dist = ret.star.distance(curr);
                        user.history[user.jumpnum].resultnum++;
                    }
                    //cleanup, prep next jump
                    conn.Close();
                    user.history[user.jumpnum].time = DateTime.Now - start;
                    if (user.history[user.jumpnum].resultnum < 15 && user.query < 10)
                        user.query++;
                    else if (user.history[user.jumpnum].resultnum > 100 && user.query > 0)
                        user.query--;
                    user.jumpnum++;
                }
                else
                    Thread.Sleep(((user.lastjump.Add(user.next_query_time) - DateTime.Now).TotalSeconds > new TimeSpan(0,0,2).TotalSeconds ? (user.lastjump.Add(user.next_query_time) - DateTime.Now) : new TimeSpan(1)));
            }
            return user;
        }
    }
}
