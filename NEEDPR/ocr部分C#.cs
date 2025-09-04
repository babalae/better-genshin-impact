using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenCvSharp;

// 作者环境 net9， 控制台模板，使用了message，
//OpenCvSharp  版本4.10.0.20241108   OpenCvSharp4.runtime.win    4.10.0.20241108


namespace test
{
    public class 主程序
    {
        static void Main()
        {
            提取 初始化对象_提取 = new 提取();

            Mat 读取的图片 = Cv2.ImRead(@"F:\11\5.png");

           Mat aaa= 初始化对象_提取.第二种色阶极值处理(读取的图片);
            GC.Collect();
        }
    }


    //第一种办法，使用颜色提取，缺点需要手动调整精度，且颜色是连续的值会误入一些不想关的颜色，且对于白色目前
   //本人没有测试，如果需要，可以自定义添加到hsv数组
    public class 提取
    {
        public struct Hsv数组
        {
            //定义结构体
            public Scalar 下限制 { get; set; }
            public Scalar 上限制 { get; set; }

            public Hsv数组(Scalar 写下限制, Scalar 写上限制)
            {
                下限制 = 写下限制;
                上限制 = 写上限制;
            }
        }

        public Mat hsv加颜色提取(Mat 导入的图片)
        {
            if (导入的图片 == null)
            {
                MessageBox.Show("图片为空", "error");
                throw new Exception("");
            }

            // 向结构体存放 上下限制
            List<Hsv数组> hsv判断数据 = new List<Hsv数组>
            {
                new Hsv数组(new Scalar(0, 0, 0), new Scalar(238, 0, 0))  // 全范围 HSV，涵盖所有颜色
            };

            var hsv图片 = new Mat();
            var mask合并 = new Mat(导入的图片.Size(), MatType.CV_8UC1, Scalar.All(0));
            var 圆化参数 = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(4, 4)); 
            var 平滑边 = new Mat(导入的图片.Size(), MatType.CV_8UC1); 

            try
            {
                
                Cv2.CvtColor(导入的图片, hsv图片, ColorConversionCodes.BGR2HSV);

               
                //Cv2.ImShow("HSV 图像", hsv图片);
                //Cv2.WaitKey(0);

                
                foreach (var 区间 in hsv判断数据)
                {
                   
                    Mat 下限制Mat = new Mat(1, 1, MatType.CV_8UC3, 区间.下限制);
                    Mat 上限制Mat = new Mat(1, 1, MatType.CV_8UC3, 区间.上限制);

                   
                    Cv2.InRange(hsv图片, 下限制Mat, 上限制Mat, mask合并);

                   
                }

                //Cv2.ImShow("合并后的mask", mask合并);
                //Cv2.WaitKey(0);
                Cv2.MorphologyEx(mask合并, 平滑边, MorphTypes.Close, 圆化参数, iterations: 3);
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "error", MessageBoxButtons.OKCancel);
                throw;
            }
            finally
            {
                hsv图片.Dispose();
               
            }

            Cv2.ImShow("最终结果", mask合并);
            Cv2.WaitKey(0);
            return mask合并;
        }



        public Mat 色阶极值处理(Mat 导入的图片)
        {
            // 声明， 该方法作者本人也不知道什么原理，但是效果就是要比颜色提取好，如果效果不好，请先提亮图片30%以上，拉大黑场
            // 这里只实现强对比，如果需要 完整的ps逻辑，实现高效运算 参考ps原理 该网址附带有py演示https://blog.csdn.net/u011520181/article/details/114133219

            int 输入黑场 = 0;   
            int 输入白场 = 255;  
            int 输出黑场 = 0;    
            int 输出白场 = 255;  

            double gama = 55;   //midtone: 这里 


            byte[] 查找表 = new byte[256];

            for (int i = 0; i < 256; i++)
            {
                double v;

                if (i <= 输入黑场)
                {
                    v = (double)输出黑场;
                }
                else if (i >= 输入白场)
                {
                    v = (double)输出白场;
                }
                else
                {
                    
                    v = (double)(i - 输入黑场) / (输入白场 - 输入黑场);
                    v = Math.Pow(v, gama); 
                    v = v * (输出白场 - 输出黑场) + 输出黑场;
                }
                查找表[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
            }

  
            Mat lutMat = Mat.FromArray(查找表);

            Mat dst = new Mat();
            Cv2.LUT(导入的图片, lutMat, dst);

            // 显示结果
            Cv2.ImShow("result", dst);
            Cv2.WaitKey(0);
            Cv2.DestroyAllWindows(); 

            return dst;
        }

        // 参考PS的线性提亮
        // 利用线性提亮可以增加文字（默认其亮度都会比其周围的高），使得色阶处理之后文字更佳明显
        private Mat 线性提亮( Mat 导入的图片)
        {

            if (导入的图片 == null)
            {
                MessageBox.Show("没有导入图片");
                throw new Exception("error");
            }
            double alpha = 1.3; // 30%亮度
            int beta = 50;

            Mat 结果图片 = new Mat(导入的图片.Size(), 导入的图片.Type());



            for (int y = 0; y < 导入的图片.Rows; y++)
            {
                for (int x = 0; x < 导入的图片.Cols; x++)
                {
                    // 获取原图像素
                    Vec3b pixel = 导入的图片.At<Vec3b>(y, x);

                    // 对每个通道应用线性变换
                    for (int c = 0; c < 导入的图片.Channels(); c++)
                    {
                        // 线性变换：alpha * src + beta
                        int newVal = (int)(alpha * pixel[c] + beta);

                        // 确保像素值在 [0, 255] 范围内
                        newVal = Math.Clamp(newVal, 0, 255);

                        // 设置新图像的像素值
                       结果图片.At<Vec3b>(y, x)[c] = (byte)newVal;
                    }
                }
            }


            Cv2.ImShow("result", 结果图片);
            Cv2.WaitKey(0);
            return 结果图片;

        }
        public Mat 第二种色阶极值处理( Mat 导入的图片 ){
            //该表现更符合ps的逻辑 https://blog.csdn.net/u011520181/article/details/114133219

            double 输入黑场 = 0;
            double 输入白场 = 255;
            double 输出黑场 = 0;
            double 输出白场 = 255;

            double 中间调 = 0.01;

            byte[] 查找表 = new byte[256];

            for (int i = 0; i < 256; i++){

                double Vin = i; //像素输入值

                double v_step1 = 255.0 * (Vin - 输入黑场) / (输入白场 - 输入黑场);
                v_step1 = Math.Max(0,Math.Min(255,v_step1));

                double v_step2 = 255.0 * Math.Pow(v_step1 / 255.0, 1.0 / 中间调);
                v_step2 = Math.Max(0, Math.Min(255, v_step2));

                double v_step3 = (v_step2 / 255.0) * (输出白场 -输出黑场) + 输出黑场;
                v_step3 = Math.Max(0, Math.Min(255, v_step3));

                查找表[i] = (byte)Math.Round(v_step3);

            }

            Mat lutMat = Mat.FromArray(查找表);


            Mat dst = new Mat();
            Cv2.LUT(导入的图片, lutMat, dst);

            Cv2.ImShow("result", dst);
            Cv2.WaitKey(0);

            return dst;

        }


    }


}
