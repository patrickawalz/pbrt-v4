// args_test.cpp

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ArgsTests
    {
        [TEST("Args", "Simple")]
        public static void Args_Simple()
        {
            Action<string> expectNoError = (string s) => { ASSERT_FALSE(true); };

            {
                int nthreads = 0;
                List<string> args = SplitStringsFromWhitespace("--nthreads 4");
                int iter = 0;
                EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, expectNoError));
                EXPECT_EQ(nthreads, 4);
                EXPECT_TRUE(++iter == args.Count);
            }
            {
                int nthreads = 0;
                List<string> args = SplitStringsFromWhitespace("--nthreads=4");
                int iter = 0;
                EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, expectNoError));
                EXPECT_EQ(nthreads, 4);
                EXPECT_TRUE(++iter == args.Count);
            }
        }

        [TEST("Args", "Multiple")]
        public static void Args_Multiple()
        {
            Action<string> expectNoError = (string s) => { ASSERT_FALSE(true); };

            {
                int nthreads = 0;
                bool log = false;
                List<string> args = SplitStringsFromWhitespace("--log --nthreads 4 yolo");
                int iter = 0;
                EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "log", ref log, expectNoError));
                ++iter;
                EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, expectNoError));
                EXPECT_EQ(nthreads, 4);
                EXPECT_TRUE(log);
                EXPECT_FALSE(++iter == args.Count);
                EXPECT_EQ(args[iter], "yolo");
            }
            {
                int nthreads = 0;
                List<string> args = SplitStringsFromWhitespace("yolo --nthreads=4");
                int iter = 0;
                EXPECT_FALSE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, expectNoError));
                EXPECT_EQ(args[iter], "yolo");
                ++iter;
                EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, expectNoError));
                EXPECT_EQ(nthreads, 4);
                EXPECT_TRUE(++iter == args.Count);
            }
        }

        [TEST("Args", "Bool")]
        public static void Args_Bool()
        {
            Action<string> expectNoError = (string s) => { ASSERT_FALSE(true); };

            bool log = true;
            bool benchmark = false;
            bool debug = false;
            List<string> args = SplitStringsFromWhitespace("--log=false --benchmark --debug=true");
            int iter = 0;
            EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "log", ref log, expectNoError));
            EXPECT_FALSE(log);
            ++iter;
            EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "benchmark", ref benchmark, expectNoError));
            EXPECT_TRUE(benchmark);
            ++iter;
            EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "debug", ref debug, expectNoError));
            EXPECT_TRUE(debug);
            EXPECT_TRUE(++iter == args.Count);
        }

        [TEST("Args", "ErrorMissingValue")]
        public static void Args_ErrorMissingValue()
        {
            bool errCalled = false;
            Action<string> callback = (string s) => { errCalled = true; };

            int nthreads = 2;
            List<string> args = SplitStringsFromWhitespace("--nthreads");
            int iter = 0;
            EXPECT_FALSE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, callback));
            EXPECT_EQ(nthreads, 2);
            EXPECT_TRUE(errCalled);
        }

        [TEST("Args", "ErrorMissingValueEqual")]
        public static void Args_ErrorMissingValueEqual()
        {
            bool errCalled = false;
            Action<string> callback = (string s) => { errCalled = true; };

            int nthreads = 2;
            List<string> args = SplitStringsFromWhitespace("--nthreads=");
            int iter = 0;
            EXPECT_FALSE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, callback));
            EXPECT_EQ(nthreads, 2);
            EXPECT_TRUE(errCalled);
        }

        [TEST("Args", "ErrorBogusBool")]
        public static void Args_ErrorBogusBool()
        {
            bool errCalled = false;
            Action<string> callback = (string s) => { errCalled = true; };

            bool log = false;
            List<string> args = SplitStringsFromWhitespace("--log=tru3");
            int iter = 0;
            EXPECT_FALSE(ParseArg(args, ref iter, args.Count, "log", ref log, callback));
            EXPECT_FALSE(log);
            EXPECT_TRUE(errCalled);
        }

        [TEST("Args", "Normalization")]
        public static void Args_Normalization()
        {
            Action<string> expectNoError = (string s) => { ASSERT_FALSE(true); };

            {
                int nthreads = 0;
                List<string> args = SplitStringsFromWhitespace("--n_threads 4");
                int iter = 0;
                EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, expectNoError));
                EXPECT_EQ(nthreads, 4);
                EXPECT_TRUE(++iter == args.Count);
            }
            {
                int nthreads = 0;
                List<string> args = SplitStringsFromWhitespace("--nThreads=4");
                int iter = 0;
                EXPECT_TRUE(ParseArg(args, ref iter, args.Count, "nthreads", ref nthreads, expectNoError));
                EXPECT_EQ(nthreads, 4);
                EXPECT_TRUE(++iter == args.Count);
            }
        }
    }
}
