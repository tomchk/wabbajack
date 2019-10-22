﻿using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Wabbajack.Common;
using Wabbajack.Lib;

namespace Wabbajack
{
    public class MainWindowVM : ViewModel
    {
        public AppState AppState { get; }

        private ViewModel _ActivePane;
        public ViewModel ActivePane { get => _ActivePane; set => this.RaiseAndSetIfChanged(ref _ActivePane, value); }

        private int _QueueProgress;
        public int QueueProgress { get => _QueueProgress; set => this.RaiseAndSetIfChanged(ref _QueueProgress, value); }

        private readonly Subject<CPUStatus> _statusSubject = new Subject<CPUStatus>();
        public IObservable<CPUStatus> StatusObservable => _statusSubject;
        public ObservableCollectionExtended<CPUStatus> StatusList { get; } = new ObservableCollectionExtended<CPUStatus>();

        public MainWindowVM(RunMode mode)
        {
            this.AppState = new AppState(this, mode);

            // Initialize work queue
            WorkQueue.Init(
                report_function: (id, msg, progress) => this._statusSubject.OnNext(new CPUStatus() { ID = id, Msg = msg, Progress = progress }),
                report_queue_size: (max, current) => this.SetQueueSize(max, current));

            // Compile progress updates and populate ObservableCollection
            this._statusSubject
                .ObserveOn(RxApp.TaskpoolScheduler)
                .ToObservableChangeSet(x => x.ID)
                .Batch(TimeSpan.FromMilliseconds(250))
                .EnsureUniqueChanges()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Sort(SortExpressionComparer<CPUStatus>.Ascending(s => s.ID), SortOptimisations.ComparesImmutableValuesOnly)
                .Bind(this.StatusList)
                .Subscribe()
                .DisposeWith(this.CompositeDisposable);
        }

        private void SetQueueSize(int max, int current)
        {
            if (max == 0)
                max = 1;
            QueueProgress = current * 100 / max;
        }
    }
}
