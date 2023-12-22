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

                foreach (Entity e in Scene.current.entities)
                {
                    ImGui.PushID(e.name + Scene.current.entities.IndexOf(e));

                    if (ImGui.TreeNode(e.name))
                    {
                        if (ImGui.TreeNode("Transform"))
                        {
                            Transform t = e.transform;
                            System.Numerics.Vector3 pos = new System.Numerics.Vector3(t.pos.X, t.pos.Y, t.pos.Z);
                            System.Numerics.Vector3 rot = new System.Numerics.Vector3(t.rot.X, t.rot.Y, t.rot.Z);
                            System.Numerics.Vector3 scale = new System.Numerics.Vector3(t.scale.X, t.scale.Y, t.scale.Z);

                            ImGui.DragFloat3("Pos", ref pos, 0.1f);
                            e.transform.pos = new Vec3(pos.X, pos.Y, pos.Z);

                            ImGui.DragFloat3("Rot", ref rot, 0.1f);
                            e.transform.rot = new Vec3(rot.X, rot.Y, rot.Z);

                            ImGui.DragFloat3("Scale", ref scale, 0.1f);
                            e.transform.scale = new Vec3(scale.X, scale.Y, scale.Z);

                            ImGui.TreePop();
                        }

                        if(e.HasComponent<SpriteRenderer>())
                        {
                            if (ImGui.TreeNode("Sprite Renderer"))
                            {
                                Color sprColor = e.GetComponent<SpriteRenderer>().sprite.color;
                                System.Numerics.Vector4 color = new System.Numerics.Vector4(sprColor.r, sprColor.g, sprColor.b, sprColor.a);

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

            string playButtonString = Application.isPlaying ? "Stop" : "Play";

            if (draw)
            {
                ImGui.BeginMainMenuBar();
                    ImGui.BeginMenu("File");
                    ImGui.BeginMenu("Edit");
                    ImGui.BeginMenu("Preferences");
                    ImGui.BeginMenu("Window");
                if (ImGui.BeginMenu(playButtonString))
                {
                    Application.isPlaying = !Application.isPlaying;
                    
                    if(!Application.isPlaying)
                        Scene.LoadScene(Scene.current);
                }
                    
                    ImGui.BeginMenu("Help");
                ImGui.EndMainMenuBar();

            }
        }
    }
}
