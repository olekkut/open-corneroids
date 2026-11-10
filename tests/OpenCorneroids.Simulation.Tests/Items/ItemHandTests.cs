using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Items;

public class ItemHandTests
{
    const int A = 7; // stack limit 5
    const int B = 8; // stack limit 10

    static Inventory With(params ItemStack?[] contents)
    {
        var inventory = new Inventory(contents.Length, id => id == A ? 5 : 10);
        for (var i = 0; i < contents.Length; i++)
            inventory.SetSlot(i, contents[i]);
        return inventory;
    }

    static ItemHand Holding(Inventory scratch, ItemStack stack)
    {
        // Picks the stack up from a scratch inventory, the only way a hand gets anything from a slot.
        var hand = new ItemHand();
        scratch.SetSlot(0, stack);
        hand.Click(scratch, 0, HandButton.Left);
        return hand;
    }

    static ItemStack S(int id, int quantity) => new(id, quantity);

    // --- empty hand ---

    [Fact]
    public void Left_click_with_an_empty_hand_takes_the_whole_stack()
    {
        var inventory = With(S(B, 7));
        var hand = new ItemHand();

        Assert.True(hand.Click(inventory, 0, HandButton.Left));

        Assert.Equal(S(B, 7), hand.Held);
        Assert.Null(inventory.Slots[0]);
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(4, 2)]
    [InlineData(3, 1)]
    [InlineData(1, 1)]
    public void Right_click_with_an_empty_hand_takes_half_rounded_down_and_a_single_item_whole(int count, int taken)
    {
        var inventory = With(S(B, count));
        var hand = new ItemHand();

        hand.Click(inventory, 0, HandButton.Right);

        Assert.Equal(S(B, taken), hand.Held);
        Assert.Equal(count == taken ? null : S(B, count - taken), inventory.Slots[0]);
    }

    [Fact]
    public void A_click_on_an_empty_slot_with_an_empty_hand_does_nothing()
    {
        var inventory = With((ItemStack?)null);
        var hand = new ItemHand();

        Assert.False(hand.Click(inventory, 0, HandButton.Left));
        Assert.False(hand.Click(inventory, 0, HandButton.Right));
        Assert.True(hand.IsEmpty);
    }

    // --- holding, left click ---

    [Fact]
    public void Left_click_on_an_empty_slot_puts_the_whole_stack()
    {
        var inventory = With(S(B, 6), null);
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        hand.Click(inventory, 1, HandButton.Left);

        Assert.True(hand.IsEmpty);
        Assert.Equal([null, S(B, 6)], inventory.Slots);
    }

    [Fact]
    public void Left_click_on_the_same_item_tops_it_up_and_keeps_the_rest_in_the_hand()
    {
        var inventory = With(S(A, 4), S(A, 3));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        hand.Click(inventory, 1, HandButton.Left);

        Assert.Equal(S(A, 5), inventory.Slots[1]);
        Assert.Equal(S(A, 2), hand.Held);
    }

    [Fact]
    public void Left_click_on_a_different_item_swaps_hand_and_slot()
    {
        var inventory = With(S(A, 4), S(B, 9));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        hand.Click(inventory, 1, HandButton.Left);

        Assert.Equal(S(A, 4), inventory.Slots[1]);
        Assert.Equal(S(B, 9), hand.Held);
    }

    [Fact]
    public void Left_click_on_a_full_stack_of_the_same_item_swaps_them()
    {
        var inventory = With(S(A, 2), S(A, 5));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        hand.Click(inventory, 1, HandButton.Left);

        Assert.Equal(S(A, 2), inventory.Slots[1]);
        Assert.Equal(S(A, 5), hand.Held);
    }

    [Fact]
    public void A_swap_into_a_slot_that_cannot_hold_the_whole_hand_does_nothing()
    {
        // The hand took 10 of B from a bigger inventory; this one stacks B only to 5.
        var hand = Holding(new Inventory(1, _ => 10), S(B, 10));
        var inventory = new Inventory(1, _ => 5);
        inventory.SetSlot(0, S(A, 3));

        Assert.False(hand.Click(inventory, 0, HandButton.Left));

        Assert.Equal(S(B, 10), hand.Held);
        Assert.Equal(S(A, 3), inventory.Slots[0]);
    }

    // --- holding, right click ---

    [Fact]
    public void Right_click_puts_one_onto_an_empty_slot_or_the_same_item_with_room()
    {
        var inventory = With(S(B, 4), null, S(B, 2));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        hand.Click(inventory, 1, HandButton.Right);
        hand.Click(inventory, 2, HandButton.Right);

        Assert.Equal([null, S(B, 1), S(B, 3)], inventory.Slots);
        Assert.Equal(S(B, 2), hand.Held);
    }

    [Fact]
    public void Right_click_puts_the_last_one_and_empties_the_hand()
    {
        var inventory = With(S(B, 1), null);
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        hand.Click(inventory, 1, HandButton.Right);

        Assert.True(hand.IsEmpty);
        Assert.Equal(S(B, 1), inventory.Slots[1]);
    }

    [Fact]
    public void Right_click_on_a_full_stack_does_nothing()
    {
        var inventory = With(S(A, 2), S(A, 5));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        Assert.False(hand.Click(inventory, 1, HandButton.Right));

        Assert.Equal(S(A, 2), hand.Held);
        Assert.Equal(S(A, 5), inventory.Slots[1]);
    }

    [Fact]
    public void Right_click_on_a_different_item_does_nothing_and_loses_nothing_unlike_the_original()
    {
        var inventory = With(S(A, 2), S(B, 3));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        Assert.False(hand.Click(inventory, 1, HandButton.Right));

        Assert.Equal(S(A, 2), hand.Held);
        Assert.Equal(S(B, 3), inventory.Slots[1]);
    }

    // --- between windows, trash, endless slots ---

    [Fact]
    public void A_stack_moves_from_one_inventory_to_another_through_the_hand()
    {
        var player = With(S(B, 8), null);
        var container = With(null, S(A, 1));
        var hand = new ItemHand();

        hand.Click(player, 0, HandButton.Left);
        hand.Click(container, 0, HandButton.Left);

        Assert.True(hand.IsEmpty);
        Assert.Null(player.Slots[0]);
        Assert.Equal(S(B, 8), container.Slots[0]);
    }

    [Fact]
    public void The_trash_takes_the_held_stack_and_gives_it_back_to_an_empty_hand()
    {
        var inventory = With(S(B, 4));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        hand.ClickTrash(inventory);
        Assert.True(hand.IsEmpty);
        Assert.Equal(S(B, 4), inventory.Trash);

        hand.ClickTrash(inventory);
        Assert.Equal(S(B, 4), hand.Held);
        Assert.Null(inventory.Trash);
    }

    [Fact]
    public void An_endless_slot_gives_one_at_a_time_up_to_the_stack_limit()
    {
        var hand = new ItemHand();

        Assert.True(hand.TakeEndless(A, 2));
        Assert.True(hand.TakeEndless(A, 2));
        Assert.False(hand.TakeEndless(A, 2));
        Assert.Equal(S(A, 2), hand.Held);
        Assert.False(hand.TakeEndless(B, 10));
    }

    [Fact]
    public void Returning_the_hand_puts_back_what_fits_and_keeps_the_rest()
    {
        var hand = Holding(new Inventory(1, _ => 10), S(B, 10));
        var inventory = With(S(B, 7));

        Assert.False(hand.ReturnTo(inventory));

        Assert.Equal(S(B, 10), inventory.Slots[0]);
        Assert.Equal(S(B, 7), hand.Held);
    }

    [Fact]
    public void Release_empties_the_hand_and_returns_the_stack()
    {
        var hand = Holding(With((ItemStack?)null), S(B, 3));
        int changes = 0;
        hand.Changed += () => changes++;

        Assert.Equal(S(B, 3), hand.Release());
        Assert.True(hand.IsEmpty);
        Assert.Null(hand.Release());
        Assert.Equal(1, changes);
    }

    // --- equipment slots ---

    static (Suit Suit, Inventory Inventory) Suited(params ItemStack?[] contents)
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, Math.Max(2, contents.Length));
        for (int i = 0; i < contents.Length; i++) inventory.SetSlot(i, contents[i]);
        return (new Suit(set), inventory);
    }

    [Fact]
    public void A_held_piece_goes_on_and_an_empty_hand_takes_it_off()
    {
        var (suit, inventory) = Suited(S(Helmet, 1));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        Assert.Equal(WornClick.Changed, hand.ClickWorn(suit, WearableSlot.Helmet, inventory));
        Assert.True(hand.IsEmpty);
        Assert.Equal(S(Helmet, 1), suit.Equipment[(int)WearableSlot.Helmet]);
        Assert.Equal(110, suit.MaxHealth);

        Assert.Equal(WornClick.Changed, hand.ClickWorn(suit, WearableSlot.Helmet, inventory));
        Assert.Equal(S(Helmet, 1), hand.Held);
        Assert.Null(suit.Equipment[(int)WearableSlot.Helmet]);
    }

    [Fact]
    public void A_piece_for_another_slot_or_an_item_that_cannot_be_worn_stays_in_the_hand()
    {
        var (suit, inventory) = Suited(S(Battery, 1), S(Rock, 5));
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);
        Assert.Equal(WornClick.WrongSlot, hand.ClickWorn(suit, WearableSlot.Helmet, inventory));

        hand.Click(inventory, 0, HandButton.Left);
        hand.Click(inventory, 1, HandButton.Left);   // swap the battery for the rock
        Assert.Equal(WornClick.NotWearable, hand.ClickWorn(suit, WearableSlot.Helmet, inventory));
        Assert.Equal(S(Rock, 5), hand.Held);
        Assert.All(suit.Equipment, Assert.Null);
    }

    const int OtherHelmet = 32;

    /// <summary>The test itemset plus a second helmet, so one helmet can be worn over another.</summary>
    static ItemsetDef TwoHelmets()
    {
        var set = Set();
        var items = set.Items.Append(Item(OtherHelmet, "wearable", 1, components: [new WearableComponent(WearableSlot.Helmet, 1, 0, 0)])).ToArray();
        return new ItemsetDef
        {
            Path = set.Path, Header = set.Header, Items = items, ItemsById = items.ToDictionary(i => i.ItemId),
            ItemsByBlockId = set.ItemsByBlockId, Projectiles = set.Projectiles, Blueprints = set.Blueprints,
            DefaultItems = set.DefaultItems, Fingerprint = set.Fingerprint,
        };
    }

    [Fact]
    public void Wearing_the_same_piece_again_does_nothing()
    {
        var (suit, inventory) = Suited(S(Helmet, 1));
        suit.TryExchange(WearableSlot.Helmet, S(Helmet, 1), out _);
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        Assert.Equal(WornClick.Nothing, hand.ClickWorn(suit, WearableSlot.Helmet, inventory));
        Assert.Equal(S(Helmet, 1), hand.Held);
    }

    [Fact]
    public void A_single_piece_swaps_with_the_worn_one_through_the_hand()
    {
        var set = TwoHelmets();
        var suit = new Suit(set);
        var inventory = Inventory.ForItemset(set, 2);
        inventory.SetSlot(0, S(OtherHelmet, 1));
        suit.TryExchange(WearableSlot.Helmet, S(Helmet, 1), out _);
        var hand = new ItemHand();
        hand.Click(inventory, 0, HandButton.Left);

        Assert.Equal(WornClick.Changed, hand.ClickWorn(suit, WearableSlot.Helmet, inventory));

        Assert.Equal(S(Helmet, 1), hand.Held);
        Assert.Equal(S(OtherHelmet, 1), suit.Equipment[(int)WearableSlot.Helmet]);
    }

    [Fact]
    public void Wearing_from_a_hand_of_more_than_one_sends_the_old_piece_to_the_inventory_or_refuses()
    {
        // A modded itemset may stack wearables: the hand holds two of one helmet while another is worn.
        var set = TwoHelmets();
        var suit = new Suit(set);
        var full = Inventory.ForItemset(set, 1);
        full.SetSlot(0, S(Rock, 64));
        suit.TryExchange(WearableSlot.Helmet, S(OtherHelmet, 1), out _);
        var hand = Holding(new Inventory(1, _ => 5), S(Helmet, 2));

        Assert.Equal(WornClick.NoRoom, hand.ClickWorn(suit, WearableSlot.Helmet, full));
        Assert.Equal(S(Helmet, 2), hand.Held);
        Assert.Equal(S(OtherHelmet, 1), suit.Equipment[(int)WearableSlot.Helmet]);

        var room = Inventory.ForItemset(set, 1);
        Assert.Equal(WornClick.Changed, hand.ClickWorn(suit, WearableSlot.Helmet, room));
        Assert.Equal(S(Helmet, 1), hand.Held);
        Assert.Equal(S(Helmet, 1), suit.Equipment[(int)WearableSlot.Helmet]);
        Assert.Equal(S(OtherHelmet, 1), room.Slots[0]);
    }

    [Fact]
    public void The_suit_only_takes_a_single_item_worn_in_that_slot()
    {
        var suit = new Suit(Set());

        Assert.False(suit.TryExchange(WearableSlot.Helmet, S(Helmet, 2), out _));
        Assert.False(suit.TryExchange(WearableSlot.Chest, S(Helmet, 1), out _));
        Assert.False(suit.TryExchange(WearableSlot.Helmet, S(Rock, 1), out _));
        Assert.All(suit.Equipment, Assert.Null);
    }

    // --- conservation ---

    [Fact]
    public void Random_clicks_never_change_the_number_of_items()
    {
        var rng = new Random(24092004);
        for (int run = 0; run < 200; run++)
        {
            var player = new Inventory(6, id => id == A ? 5 : 10);
            var container = new Inventory(4, id => id == A ? 5 : 10);
            foreach (var inventory in new[] { player, container })
                for (int i = 0; i < inventory.Slots.Count; i++)
                    if (rng.Next(3) > 0)
                    {
                        int id = rng.Next(2) == 0 ? A : B;
                        inventory.SetSlot(i, S(id, rng.Next(1, inventory.MaxStackOf(id) + 1)));
                    }
            var hand = new ItemHand();
            var before = Totals(player, container, hand);

            for (int click = 0; click < 100; click++)
            {
                var target = rng.Next(2) == 0 ? player : container;
                hand.Click(target, rng.Next(target.Slots.Count), rng.Next(2) == 0 ? HandButton.Left : HandButton.Right);
                Assert.Equal(before, Totals(player, container, hand));
                Assert.All(player.Slots.Concat(container.Slots), s => Assert.True(s is null || (s.Quantity > 0 && s.Quantity <= player.MaxStackOf(s.ItemId))));
            }
            hand.ReturnTo(player);
            hand.ReturnTo(container);
            Assert.Equal(before, Totals(player, container, hand));
        }
    }

    [Fact]
    public void Random_clicks_with_the_suit_trash_and_endless_slots_and_different_stack_limits_keep_every_item()
    {
        int[] ids = [Rock, Plate, Helmet, Battery];
        var rng = new Random(20121109);
        for (int run = 0; run < 200; run++)
        {
            // Different stack limits on each side, and wearables that stack, so swaps and the suit meet oversized stacks.
            var player = new Inventory(6, id => id switch { Rock => 5, Plate => 10, Helmet => 3, _ => 2 });
            var container = new Inventory(4, id => id switch { Rock => 7, Plate => 4, Helmet => 1, _ => 3 });
            foreach (var inventory in new[] { player, container })
                for (int i = 0; i < inventory.Slots.Count; i++)
                    if (rng.Next(3) > 0)
                    {
                        int id = ids[rng.Next(ids.Length)];
                        inventory.SetSlot(i, S(id, rng.Next(1, inventory.MaxStackOf(id) + 1)));
                    }
            var suit = new Suit(Set());
            var hand = new ItemHand();
            var expected = ids.ToDictionary(id => id, id => Count(id));

            for (int click = 0; click < 100; click++)
            {
                switch (rng.Next(6))
                {
                    case 0 or 1 or 2:
                        var target = rng.Next(2) == 0 ? player : container;
                        hand.Click(target, rng.Next(target.Slots.Count), rng.Next(2) == 0 ? HandButton.Left : HandButton.Right);
                        break;
                    case 3:
                        hand.ClickWorn(suit, (WearableSlot)rng.Next(suit.Equipment.Count), player);
                        break;
                    case 4:
                        // The trash destroys what it held before; that is the only way items leave.
                        if (!hand.IsEmpty && player.Trash is { } destroyed) expected[destroyed.ItemId] -= destroyed.Quantity;
                        hand.ClickTrash(player);
                        break;
                    default:
                        int endless = rng.Next(2) == 0 ? Rock : Helmet;
                        if (hand.TakeEndless(endless, player.MaxStackOf(endless))) expected[endless]++;
                        break;
                }
                Assert.All(ids, id => Assert.Equal(expected[id], Count(id)));
                Assert.All(player.Slots, s => Assert.True(s is null || (s.Quantity > 0 && s.Quantity <= player.MaxStackOf(s.ItemId))));
                Assert.All(container.Slots, s => Assert.True(s is null || (s.Quantity > 0 && s.Quantity <= container.MaxStackOf(s.ItemId))));
                Assert.All(suit.Equipment, s => Assert.True(s is null || s.Quantity == 1));
            }

            long Count(int id) => player.CountFrom(id, 0) + container.CountFrom(id, 0)
                + (hand.Held?.ItemId == id ? hand.Held.Quantity : 0)
                + (player.Trash?.ItemId == id ? player.Trash.Quantity : 0)
                + suit.Equipment.Count(s => s?.ItemId == id);
        }
    }

    static (long A, long B) Totals(Inventory player, Inventory container, ItemHand hand)
    {
        long Of(int id) => player.CountFrom(id, 0) + container.CountFrom(id, 0) + (hand.Held?.ItemId == id ? hand.Held.Quantity : 0);
        return (Of(A), Of(B));
    }
}
