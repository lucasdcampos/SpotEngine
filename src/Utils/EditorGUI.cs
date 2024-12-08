// not being used
using ImGuiNET;
using SpotEngine;

namespace SpotEditor
{
    public class EditorGUI
    {
        public static void DrawInspector(bool draw)
        {
            if (draw)
            {
                ImGui.Begin("Scene Hierarchy", ref draw);

                foreach (Entity e in Scene.Active.Entities)
                {
                    ImGui.PushID(e.name + Scene.Active.Entities.IndexOf(e));

                    if (ImGui.TreeNode(e.name))
                    {
                        if (ImGui.TreeNode("Transform"))
                        {
                            Transform t = e.transform;
                            System.Numerics.Vector3 pos = new System.Numerics.Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z);
                            System.Numerics.Vector3 rot = new System.Numerics.Vector3(t.Rot.X, t.Rot.Y, t.Rot.Z);
                            System.Numerics.Vector3 scale = new System.Numerics.Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z);

                            ImGui.DragFloat3("Pos", ref pos, 0.1f);
                            e.transform.Pos = new Vec3(pos.X, pos.Y, pos.Z);

                            ImGui.DragFloat3("Rot", ref rot, 0.1f);
                            e.transform.Rot = new Vec3(rot.X, rot.Y, rot.Z);

                            ImGui.DragFloat3("Scale", ref scale, 0.1f);
                            e.transform.Scale = new Vec3(scale.X, scale.Y, scale.Z);

                            ImGui.TreePop();
                        }

                        if(e.HasComponent<SpriteRenderer>())
                        {
                            if (ImGui.TreeNode("Sprite Renderer"))
                            {
                                Color sprColor = e.GetComponent<SpriteRenderer>().sprite.color;
                                System.Numerics.Vector4 color = new System.Numerics.Vector4(sprColor.R, sprColor.G, sprColor.B, sprColor.A);

                                ImGui.ColorEdit4("Color", ref color);
                                e.GetComponent<SpriteRenderer>().sprite.color = new Color(color.X, color.Y, color.Z, color.W);
                                ImGui.TreePop();
                            }
                        }

                        if (e.HasComponent<SpriteRenderer>())
                        {
                            if (ImGui.TreeNode("Rigidbody2D"))
                            {
                                ImGui.TreePop();
                            }
                        }

                        ImGui.TreePop();
                    }

                    

                    ImGui.PopID();
                }
                

                ImGui.End();
            }
            
        }

        public static void DrawTopMenuButtons(bool draw)
        {

            string playButtonString = Application.IsPlaying ? "Stop" : "Play";

            if (draw)
            {
                ImGui.BeginMainMenuBar();
                    ImGui.BeginMenu("File");
                    ImGui.BeginMenu("Edit");
                    ImGui.BeginMenu("Preferences");
                    ImGui.BeginMenu("Window");
                if (ImGui.BeginMenu(playButtonString))
                {
                    //Application.IsPlaying = !Application.IsPlaying;
                    
                    if(!Application.IsPlaying)
                        Scene.LoadScene(Scene.Active);
                }
                    
                    ImGui.BeginMenu("Help");
                ImGui.EndMainMenuBar();

            }
        }
    }
}
