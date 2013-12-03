﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace Winterdom.Viasfora.Text {

  class RainbowTagger : ITagger<ClassificationTag>, IDisposable {
    private ITextBuffer theBuffer;
    private ClassificationTag[] rainbowTags;
    private Dictionary<char, char> braceList = new Dictionary<char, char>();
    private const int MAX_DEPTH = 4;
    private List<ITagSpan<ClassificationTag>> braceTags = null;
    private LanguageInfo language;
    private object updateLock = new object();

#pragma warning disable 67
    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;
#pragma warning restore 67

    internal RainbowTagger(
          ITextBuffer buffer,
          IClassificationTypeRegistryService registry) {
      this.theBuffer = buffer;
      rainbowTags = new ClassificationTag[MAX_DEPTH];

      for ( int i = 0; i < MAX_DEPTH; i++ ) {
        rainbowTags[i] = new ClassificationTag(
          registry.GetClassificationType(Constants.RAINBOW + (i + 1)));
      }

      SetLanguage(buffer.ContentType);

      this.theBuffer.ChangedLowPriority += this.BufferChanged;
      this.theBuffer.ContentTypeChanged += this.ContentTypeChanged;
      VsfSettings.SettingsUpdated += this.OnSettingsUpdated;

      UpdateBraceList(new SnapshotPoint(buffer.CurrentSnapshot, 0));
    }

    public void Dispose() {
      if ( theBuffer != null ) {
        VsfSettings.SettingsUpdated -= OnSettingsUpdated;
        theBuffer.ChangedLowPriority -= this.BufferChanged;
        theBuffer.ContentTypeChanged -= this.ContentTypeChanged;
        theBuffer = null;
      }
    }

    public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans) {
      if ( !VsfSettings.RainbowTagsEnabled ) yield break;
      if ( spans.Count == 0 ) {
        yield break;
      }
      ITextSnapshot snapshot = spans[0].Snapshot;
      if ( braceTags.Count > 0 && snapshot != braceTags[0].Span.Snapshot ) {
        yield break;
      }
      foreach ( var tagSpan in braceTags ) {
        foreach ( var span in spans ) {
          if ( span.OverlapsWith(tagSpan.Span) )
            yield return tagSpan;
        }
      }
    }

    private bool ContainedIn(SnapshotSpan span, NormalizedSnapshotSpanCollection spans) {
      foreach ( var sp in spans ) {
        if ( span.IntersectsWith(sp) ) {
          return true;
        }
      }
      return false;
    }

    private void UpdateBraceList(SnapshotPoint startPoint) {
      var newTags = new List<ITagSpan<ClassificationTag>>();
      ITextSnapshot snapshot = startPoint.Snapshot;

      // we always recalculate the tags from the start, but we
      // only notify of changes from startPoint - end
      BraceExtractor extractor = new BraceExtractor(
        new SnapshotPoint(snapshot, 0), language);
      var braces = extractor.All();
      foreach ( var tagSpan in LookForMatchingPairs(snapshot, braces) ) {
        newTags.Add(tagSpan);
      }
      SynchronousUpdate(startPoint, newTags);
    }

    private void SynchronousUpdate(SnapshotPoint startPoint, List<ITagSpan<ClassificationTag>> newTags) {
      lock ( updateLock ) {
        this.braceTags = newTags;
        NotifyUpdateTags(startPoint.Snapshot, startPoint.Position);
      }
    }

    class Pair {
      public char Brace { get; set; }
      public int Depth { get; set; }
      public int Open { get; set; }
    }

    private IEnumerable<ITagSpan<ClassificationTag>> LookForMatchingPairs(
          ITextSnapshot snapshot, IEnumerable<SnapshotPoint> braces) {
      Stack<Pair> pairs = new Stack<Pair>();

      foreach ( var pt in braces ) {
        char ch = pt.GetChar();
        if ( IsOpeningBrace(ch) ) {
          Pair p = new Pair {
            Brace = ch, Depth = pairs.Count,
            Open = pt.Position
         };
          pairs.Push(p);
          // yield opening brace
          var tag = this.rainbowTags[p.Depth % MAX_DEPTH];
          var span = new SnapshotSpan(snapshot, p.Open, 1);
          yield return new TagSpan<ClassificationTag>(span, tag);
        } else if ( IsClosingBrace(ch) && pairs.Count > 0 ) {
          Pair p = pairs.Peek();
          if ( braceList[p.Brace] == ch ) {
            // yield closing brace
            pairs.Pop();
            var tag = this.rainbowTags[p.Depth % MAX_DEPTH];
            var span = new SnapshotSpan(snapshot, pt.Position, 1);
            yield return new TagSpan<ClassificationTag>(span, tag);
          }
        }
      }
    }

    private bool IsClosingBrace(char ch) {
      return braceList.Values.Contains(ch);
    }

    private bool IsOpeningBrace(char ch) {
      return braceList.ContainsKey(ch);
    }

    private void SetLanguage(IContentType contentType) {
      language = VsfPackage.LookupLanguage(contentType);
      this.braceList.Clear();
      String braceChars = language.BraceList;
      for ( int i = 0; i < braceChars.Length; i += 2 ) {
        this.braceList.Add(braceChars[i], braceChars[i + 1]);
      }
    }


    void OnSettingsUpdated(object sender, EventArgs e) {
      this.UpdateBraceList(new SnapshotPoint(this.theBuffer.CurrentSnapshot, 0));
    }

    private void BufferChanged(object sender, TextContentChangedEventArgs e) {
      if ( VsfSettings.RainbowTagsEnabled ) {
        if ( e.Changes.Count > 0 ) {
          UpdateBraceList(new SnapshotPoint(e.After, e.Changes[0].NewSpan.Start));
        }
      }
    }

    private void ContentTypeChanged(object sender, ContentTypeChangedEventArgs e) {
      if ( e.BeforeContentType.TypeName != e.AfterContentType.TypeName ) {
        this.SetLanguage(e.AfterContentType);
        this.UpdateBraceList(new SnapshotPoint(e.After, 0));
      }
    }

    private bool ChangeOfInterest(ITextChange change) {
      return ChangeOfInterest(change.NewText) ||
             ChangeOfInterest(change.OldText);
    }

    private bool ChangeOfInterest(String change) {
      // only recalculate everything if the change
      // contains either a brace, a quote char,
      // or an escape char
      foreach ( char ch in change ) {
        if ( language.IsSignificantSyntaxChar(ch) )
          return true;
      }
      return false;
    }

    private void NotifyUpdateTags(ITextSnapshot snapshot, int startPosition) {
      var tempEvent = TagsChanged;
      if ( tempEvent != null ) {
        tempEvent(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, startPosition,
            snapshot.Length - startPosition)));
      }
    }
  }
}