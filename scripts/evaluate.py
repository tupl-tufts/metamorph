import re
import subprocess
import sys
import psutil
import time
import argparse
from collections import defaultdict
import matplotlib.pyplot as plt
import matplotlib
import numpy as np

# Use Type-1 fonts instead of Type-3
matplotlib.rcParams['pdf.fonttype'] = 42  # Ensures TrueType fonts are used in PDFs
matplotlib.rcParams['ps.fonttype'] = 42   # Ensures TrueType fonts are used in PS

# Set a font that works well
matplotlib.rcParams['font.family'] = 'serif'  # Or 'Times New Roman' for IEEE submissions
matplotlib.rcParams['text.usetex'] = False    # Disable LaTeX rendering to avoid unexpected Type-3 fonts

DEFAULT_TIME_LIMIT = 1440
RESULTS_FILE = "cache/Results.csv"
PRETRAINED_DIR = "cache/pretraining"
BASELINE_CACHE = "cache/baseline"
EVM_BENCHMARK = "EVM"
BENCHMARKS_FIGURE_8 = ["FreezableArray", "BinaryTree", "SocialNetwork", "Firewall", "DoublyLinkedList", "Queue"]
BENCHMARKS_FIGURE_10 = ["FreezableArrayMod", "SocialNetworkMod"]
BENCHMARKS_DIR = "Benchmarks"
PROBLEM_INDEX_FILE_NAME = "ProblemIndex.csv"
METAMORPH = "dotnet Metamorph/Binaries/Metamorph.dll"
DAFNY = "dotnet Metamorph/dafny/Binaries/Dafny.dll"
HEADER = "Benchmark,TimeLimit,Method,Problem,Outcome,RunningTime\n"


class Outcome:
    SUCCESS = "SUCCESS"
    TIMEOUT = "TIMEOUT"
    FAILED = "FAILED"


class Result:

    def __init__(self, outcome, running_time):
        self.outcome = outcome
        self.running_time = running_time


class Method:
    BASELINE = "Baseline"
    NO_DISTANCE_METRIC = "Metamorph (No Distance Metric)"
    GREEDY_DISTANCE_METRIC = "Metamorph (Greedy Metric)"
    PIECEWISE_DISTANCE_METRIC = "Metamorph (Piecewise Metric)"
    PRETRAINING = "Pretrain"


def time_process(process, time_limit):
    process_instance = psutil.Process(process.pid)
    start_time = process_instance.create_time()
    while True:
        if (time.time() - start_time) > time_limit:
            process_instance.kill()
            break
        if process_instance.status() != "running":
            break
        time.sleep(1)
    process.wait() # make sure all output is written
    elapsed = time.time() - start_time
    if elapsed > time_limit:
        return Result(Outcome.TIMEOUT, elapsed)
    return Result(Outcome.SUCCESS, elapsed)


def read_results_cache():
    result = defaultdict(lambda: defaultdict(lambda: defaultdict(lambda: {})))
    with open(RESULTS_FILE, "r") as f:
        lines = f.readlines()
        for line in lines[1:]:
            benchmark, timelimit, method, problem, outcome, time = line.strip().split(",")
            time = float(time)
            timelimit = int(timelimit)
            result[benchmark][timelimit][method][problem] = Result(outcome, time)
    return result


def pretrain_metamorph(file, pretrained_dir):
    metamorph = subprocess.Popen(
        f"{METAMORPH} --input {file} --pretrain {pretrained_dir}",
        shell=True,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        universal_newlines=True)
    result = time_process(metamorph, 10_000_000)
    return result.running_time


def run_metamorph(file, time_limit, pretrained_dir, method):
    if method == Method.NO_DISTANCE_METRIC:
        extra_args = "--noDistanceMetric"
    elif method == Method.PIECEWISE_DISTANCE_METRIC:
        extra_args = f"--loadPretrained {pretrained_dir}"
    else:
        extra_args = f" --greedy"

    metamorph = subprocess.Popen(
        f"{METAMORPH} --input {file} --timeLimit {time_limit} {extra_args} > {BASELINE_CACHE}/result.txt",
        shell=True,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        universal_newlines=True)
    result = time_process(metamorph, time_limit)
    if result.outcome == Outcome.TIMEOUT:
        return result
    output = "".join(open(f"{BASELINE_CACHE}/result.txt").readlines())
    if "Failed" in output:
        return Result(Outcome.FAILED, result.running_time)
    return result


def run_baseline(config_dir, config_header, config_line, time_limit):
    template = "".join(open(config_dir + config_line[0]).readlines())
    template = re.sub(f"\\[File]", "../../" + config_dir + config_line[1], template)
    for i in range(2, len(config_header)):
        template = re.sub(f"\\[{config_header[i]}]", config_line[i], template)
    with open(f"{BASELINE_CACHE}/tmp.dfy", "w") as file:
        file.write(template)
    testGeneration = subprocess.Popen(
        f"{DAFNY} generate-tests Block "
        f"--verbose --one-test-only --verification-time-limit {time_limit} "
        f"{BASELINE_CACHE}/tmp.dfy > {BASELINE_CACHE}/tmpTests.dfy",
        shell=True,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        universal_newlines=True)
    result = time_process(testGeneration, time_limit)
    if result.outcome == Outcome.TIMEOUT:
        return result
    testing = subprocess.Popen(
        f"{DAFNY} test "
        f"{BASELINE_CACHE}/tmpTests.dfy > {BASELINE_CACHE}/tmpTestsResults.txt",
        shell=True,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        universal_newlines=True)
    testingProcess = psutil.Process(testing.pid)
    while True:
        if testingProcess.status() != "running":
            break
        time.sleep(1)
    testing.wait()
    testResults = "".join(open(f"{BASELINE_CACHE}/tmpTestsResults.txt").readlines())
    if "Synthesis goal reached" in testResults:
        return result
    return Result(Outcome.FAILED, result.running_time)


def gather_data(benchmark, method, time_limit, results_cache):
    with open(RESULTS_FILE, "a") as file:
        if method == Method.PIECEWISE_DISTANCE_METRIC:
            if Method.PRETRAINING in results_cache[benchmark][-1]:
                print(f"Using cached pretraining results.")
            else:
                print(f"Pretraining Metamorph on benchmark {benchmark}...")
                pretrain_time = pretrain_metamorph(
                    f"{BENCHMARKS_DIR}/{benchmark}/Definitions.dfy",
                    f"{PRETRAINED_DIR}/{benchmark}")
                file.write(f"{benchmark},-1,{Method.PRETRAINING},{Method.PRETRAINING},{Outcome.SUCCESS},{pretrain_time}\n")
                file.flush()
                results_cache[benchmark][-1][Method.PRETRAINING][Method.PRETRAINING] = Result(Outcome.SUCCESS,pretrain_time)
                print(f"\rPretraining Metamorph on benchmark {benchmark} took {pretrain_time} seconds...")
        config = open(f"{BENCHMARKS_DIR}/{benchmark}/{PROBLEM_INDEX_FILE_NAME}").readlines()
        config = [line.strip("\n").split(",") for line in config]
        for line in config[1:]:
            problem = line[1]
            if problem in results_cache[benchmark][time_limit][method]:
                print(f"Using cache to load the results of running {method} "
                      f"on {problem} with time limit of {time_limit} seconds.")
                if results_cache[benchmark][time_limit][method][problem].outcome == Outcome.TIMEOUT:
                    print(f"Reached a timeout, so will not process more complex problems with method {method}.")
                    break
                continue  # results already in cache
            print(f"Running {method} on {problem} with time limit of {time_limit} seconds...")
            if method == Method.BASELINE:
                result = run_baseline(f"{BENCHMARKS_DIR}/{benchmark}/", config[0], line, time_limit)
            else:
                result = run_metamorph(f"{BENCHMARKS_DIR}/{benchmark}/{problem}", time_limit, f"{PRETRAINED_DIR}/{benchmark}", method)
            file.write(f"{benchmark},{time_limit},{method},{problem},{result.outcome},{result.running_time}\n")
            file.flush()
            results_cache[benchmark][time_limit][method][problem] = result
            print(f"\rRunning {method} on {problem} with time limit of {time_limit} "
                  f"seconds took {result.running_time} seconds")
            if result.outcome == Outcome.TIMEOUT:
                print(f"Reached a timeout, so will not process more complex problems with method {method}.")
                break


def plot_evm_table(results_cache, time_limit):
    with open("Table1.csv", "w") as file:
        file.write(f"Problem,{Method.PIECEWISE_DISTANCE_METRIC},{Method.GREEDY_DISTANCE_METRIC}\n")
        problems = list(results_cache[EVM_BENCHMARK][time_limit][Method.PIECEWISE_DISTANCE_METRIC].keys())
        for problem in problems:
            file.write(f"{problem},"
                       f"{results_cache[EVM_BENCHMARK][time_limit][Method.PIECEWISE_DISTANCE_METRIC][problem].running_time},"
                       f"{results_cache[EVM_BENCHMARK][time_limit][Method.GREEDY_DISTANCE_METRIC][problem].running_time}\n")


def plot_figure(benchmark, results_cache, time_limit):
    data = {}
    for method in results_cache[benchmark][time_limit].keys():
        if method == Method.PRETRAINING:
            continue
        data[method] = ([], [])
        for problem in sorted(results_cache[benchmark][time_limit][method].keys()):
            match = re.search(r'\d+', problem)
            if not match:
               continue
            problem_id = int(match.group())
            result = results_cache[benchmark][time_limit][method][problem]
            if result.outcome == Outcome.TIMEOUT:
                result.time = time_limit
            elif result.outcome == Outcome.FAILED:
                continue
            data[method][0].append(problem_id)
            data[method][1].append(result.running_time/60)
    fig, ax = plt.subplots(layout='constrained')
    line_styles = ["-", "--", "-.", ":"]
    for id, approach in enumerate(data.keys()):
        plt.plot(data[approach][0], data[approach][1],
                 label=approach, linestyle=line_styles[id], linewidth=6)
    plt.plot(np.arange(20), [time_limit/60] * 20,
             linestyle="--", color="red", linewidth=6)
    ax.annotate("Timeout", xy=(17, 150), xytext=(
        17, 150), color="red", fontsize=36)
    
    if benchmark == "BinaryTree":
        plt.legend(loc='center right', fontsize=50, bbox_to_anchor=(1, 0.6))
    else:
        plt.legend(loc='lower right', fontsize=50)
    ax.set_ylabel('Running time (min)', fontsize=50)
    ax.set_xlabel('Object size', fontsize=50)
    ax.tick_params(axis='x', labelsize=36)
    ax.tick_params(axis='y', labelsize=36)
    fig.set_size_inches(24, 16)
    ax.set_yscale('log')
    ax.set_xticks(range(0, 20))
    # line below makes it easier to compare results compiled with different time limits
    ax.set_ylim(1/60, DEFAULT_TIME_LIMIT)
    plt.savefig(f'{benchmark}.pdf')


def main(time_limit, benchmarks):
    results_cache = read_results_cache()
    for benchmark in benchmarks:
        print(f"Processing benchmark {benchmark}")
        methods = [Method.PIECEWISE_DISTANCE_METRIC, Method.GREEDY_DISTANCE_METRIC]
        if benchmark in BENCHMARKS_FIGURE_8:
            methods += [Method.NO_DISTANCE_METRIC, Method.BASELINE]
        for method in methods:
            gather_data(benchmark, method, time_limit, results_cache)
        if benchmark == EVM_BENCHMARK:
            plot_evm_table(results_cache, time_limit)
        else:
            plot_figure(benchmark, results_cache, time_limit)


if __name__ == "__main__":
    p = argparse.ArgumentParser(
        description="Run all experiments to produce the plots for Figure 6.")
    p.add_argument("-timeLimit", type=int,
                   help=f"Time limit (in seconds) after which the process will be killed on each synthesis problem.",
                   required=True)
    figure_8_str = "Figure8"
    figure_10_str = "Figure10"
    everything_str = "ALL"
    benchmark_choices = BENCHMARKS_FIGURE_8 + BENCHMARKS_FIGURE_10 + [figure_8_str, figure_10_str, EVM_BENCHMARK, everything_str]
    p.add_argument('-benchmark', choices=benchmark_choices,
                   help=f"The benchmark to run the experiments on.",
                   required=True)
    p.add_argument("--clearCache", dest="clearCache", action="store_true",
                   help=f"Clear cache and recompute everything from scratch.")
    p.set_defaults(clearCache=False)
    args = p.parse_args(sys.argv[1:])
    if args.clearCache:
        with open(RESULTS_FILE, "w") as file:
            file.write(HEADER)
    if args.benchmark == figure_8_str:
        benchmark = BENCHMARKS_FIGURE_8
    elif args.benchmark == figure_10_str:
        benchmark = BENCHMARKS_FIGURE_10
    elif args.benchmark == everything_str:
        benchmark = BENCHMARKS_FIGURE_8 + BENCHMARKS_FIGURE_10 + [EVM_BENCHMARK]
    else:
        benchmark = [args.benchmark]
    main(args.timeLimit, benchmark)