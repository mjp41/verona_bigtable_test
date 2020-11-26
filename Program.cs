using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using Microsoft.Z3;

namespace LayoutEngine
{
    /***
     * In Verona, we need tp build a vtable for every class.
     * This is a simple example of how we could combine selector colouring
     * with row-displacement to generate an overlapping collection of vtables.
     * 
     * It basically builds a very simple constraint for Z3 to guarantee each
     * class x method has a unique entry.
     * 
     * The program is currently hard coded to a single example.
     */
    class Program
    {
        static void process_program(IEnumerable<(string, IEnumerable<string>)> prog, uint timeout_ms)
        {             
            // Switch to class name x method name list
            var program = prog.SelectMany(classdef => classdef.Item2.Select(method => (classdef.Item1, method)));

            // Set up Z3
            using (Context ctx = new Context(new Dictionary<string, string>() { { "model", "true" } }))
            {
                var z3int = ctx.MkIntSort();
                var z3zero = ctx.MkInt(0);
                var z3table_bound = ctx.MkIntConst("BOUND");
                var s = ctx.MkOptimize();

                // Build Z3 terms for each location of a method entry in the bigvtable
                var entries = program.Select(cls_method =>
                {
                    var z3cls = ctx.MkIntConst(cls_method.Item1);
                    var z3method = ctx.MkIntConst(cls_method.Item2);
                    return new { Term = ctx.MkAdd(z3cls, z3method), String = cls_method.Item1 + "::" + cls_method.Item2 };
                }
                );

                // Assert each entry is in the bounds of the table.
                var z3entries = entries.Select(entry => entry.Term).ToArray();
                foreach (var entry in z3entries)
                {
                    s.Assert(ctx.MkGe(entry, z3zero));
                    s.Assert(ctx.MkLe(entry, z3table_bound));
                }
                s.Assert(ctx.MkDistinct(z3entries));

                var classes = program.Select(cm => cm.Item1).Distinct();
                var methods = program.Select(cm => cm.Item2).Distinct();

                // Assert classes have a positive index.
                s.Add(classes.Select(cls => ctx.MkGe(ctx.MkIntConst(cls), z3zero)));
                s.Assert(ctx.MkDistinct(classes.Select(cls => ctx.MkIntConst(cls)).ToArray()));

                // Impossible to have a solution at this size.
                var fail_size = program.Count() - 2;
                // Guaranteed to have a solution at this size.
                var success_size = classes.Count() * methods.Count();

                Console.WriteLine("------------Search----------------------");
                Stopwatch sw = new Stopwatch();
                sw.Start();

                Params p = ctx.MkParams();
                p.Add("timeout", timeout_ms);
                s.Parameters = p;
                s.Assert(ctx.MkLe(z3table_bound, ctx.MkInt(success_size)));
                s.MkMinimize(z3table_bound);
                s.Check();
                sw.Stop();
                Console.WriteLine("Time Taken: " + sw.ElapsedMilliseconds + "ms");
                Console.WriteLine("------------Class Offsets---------------");

                // Print class table offsets.
                foreach (var cls in classes)
                {
                    var a = s.Model.Evaluate(ctx.MkIntConst(cls));
                    Console.WriteLine(cls + " = " + a);
                }

                Console.WriteLine("------------Method Offsets--------------");

                // Print method table offset
                foreach (var mth in methods)
                {
                    var a = s.Model.Evaluate(ctx.MkIntConst(mth));
                    Console.WriteLine(mth + " = " + a);
                }

                Console.WriteLine("------------Big table ------------------");

                // Print out the actual table
                var layout = entries.Select(term => new { slot = Convert.ToInt32(s.Model.Evaluate(term.Term).ToString()), name = term.String });
                layout = layout.OrderBy(term => term.slot);
                int topslot = 0;
                foreach (var slot in layout)
                {
                    Console.WriteLine(slot.slot + ": " + slot.name);
                    topslot = slot.slot;
                }
                Console.WriteLine("Occupancy = " + (double)layout.Count() / topslot);
            }
        }

        static IEnumerable<(string, IEnumerable<string>)> parse(string input)
        {
            foreach (var cls in input.Split("}"))
            {
                if (cls.Trim() == "") continue;
                var class_methods = cls.Split("{");
                var methods = class_methods[1].Split(";");
                yield return (class_methods[0].Trim(), methods.Select(method => method.Trim()));
            }
        }

        static void Main(string[] args)
        {
            var program = parse("C1 {m1; m2; m3; m4; m5; trace; size}"
                        + "C2 {m2; m3; trace; size }"
                        + "C3 {m3; m1; trace; size }"
                        + "C4 {trace; size}"
                        + "C5 {trace; size; m9}"
                        + "C6 {trace; size; m3}"
                        + "C7 {trace; size; m4; m5}"
                        + "C8 {trace; size; m1}"
                        + "C12 {m2; m3; trace; size }"
                        + "C13 {m3; m1; trace; size }"
                        + "C14 {trace; size}"
                        + "C15 {trace; size; m9}"
                        + "C16 {trace; size; m13}"
                        + "C17 {trace; size; m14; m5}"
                        + "C18 {trace; size; m11}"
                        );

            // Print program
            Console.WriteLine("------------Program---------------------");
            foreach (var classdef in program)
            {
                Console.WriteLine(classdef.Item1 + "{" + String.Join("; ", classdef.Item2) + "}");
            }

            process_program(program, 300);
            process_program(program, 600);
            process_program(program, 900);

        }
    }
}
